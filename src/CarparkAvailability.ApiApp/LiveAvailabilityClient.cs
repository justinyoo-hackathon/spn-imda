using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;

namespace CarparkAvailability.ApiApp;

public sealed class LiveAvailabilityClient
{
    private static readonly string[] SupportedLotTypes = ["C", "H", "S", "Y"];
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeZoneInfo SingaporeTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Singapore");

    private readonly HttpClient _httpClient;
    private readonly IHostEnvironment _environment;
    private readonly IConfiguration _configuration;
    private readonly ILogger<LiveAvailabilityClient> _logger;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private LiveAvailabilitySnapshot? _cachedSnapshot;
    private DateTimeOffset _lastAttemptAtUtc = DateTimeOffset.MinValue;

    public LiveAvailabilityClient(
        HttpClient httpClient,
        IHostEnvironment environment,
        IConfiguration configuration,
        ILogger<LiveAvailabilityClient> logger)
    {
        _httpClient = httpClient;
        _environment = environment;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<LiveAvailabilitySnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        await _refreshLock.WaitAsync(cancellationToken);

        try
        {
            if (_cachedSnapshot is not null && DateTimeOffset.UtcNow - _lastAttemptAtUtc < TimeSpan.FromSeconds(60))
            {
                return _cachedSnapshot;
            }

            _lastAttemptAtUtc = DateTimeOffset.UtcNow;

            string? apiKey = _configuration["DataGovSg:ApiKey"] ?? _configuration["DataGovSg__ApiKey"];
            if (!HasConfiguredApiKey(apiKey))
            {
                _cachedSnapshot = await LoadSampleSnapshotAsync(cancellationToken);
                return _cachedSnapshot;
            }

            try
            {
                _cachedSnapshot = await LoadLiveSnapshotAsync(apiKey!, cancellationToken);
                return _cachedSnapshot;
            }
            catch (Exception ex)
            {
                if (_cachedSnapshot is not null)
                {
                    _logger.LogWarning(ex, "Live availability refresh failed. Returning last-known-good snapshot.");

                    _cachedSnapshot = _cachedSnapshot with
                    {
                        UsingLastKnownGood = true,
                        WarningMessage = "Live availability refresh failed. Showing the last known good snapshot."
                    };

                    return _cachedSnapshot;
                }

                throw;
            }
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task<LiveAvailabilitySnapshot> LoadLiveSnapshotAsync(string apiKey, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, "/v1/transport/carpark-availability");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("x-api-key", apiKey);

        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using Stream content = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await ParseSnapshotAsync(content, usingSampleData: false, cancellationToken);
    }

    private async Task<LiveAvailabilitySnapshot> LoadSampleSnapshotAsync(CancellationToken cancellationToken)
    {
        string samplePath = ResolveDataPath("carpark-availability-sample.json");
        await using FileStream stream = File.OpenRead(samplePath);
        LiveAvailabilitySnapshot snapshot = await ParseSnapshotAsync(stream, usingSampleData: true, cancellationToken);
        return snapshot with
        {
            WarningMessage = "Using bundled sample availability data because DataGovSg__ApiKey is not configured."
        };
    }

    private async Task<LiveAvailabilitySnapshot> ParseSnapshotAsync(Stream content, bool usingSampleData, CancellationToken cancellationToken)
    {
        using JsonDocument document = await JsonDocument.ParseAsync(content, cancellationToken: cancellationToken);
        JsonElement root = document.RootElement;

        if (!root.TryGetProperty("items", out JsonElement itemsElement) || itemsElement.ValueKind != JsonValueKind.Array || itemsElement.GetArrayLength() == 0)
        {
            throw new InvalidDataException("The live availability payload does not contain any items.");
        }

        JsonElement item = itemsElement[0];
        DateTimeOffset? sourceTimestamp = TryParseDateTimeOffset(item.GetProperty("timestamp"));

        if (!item.TryGetProperty("carpark_data", out JsonElement carparksElement) || carparksElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("The live availability payload does not contain carpark data.");
        }

        Dictionary<string, LiveCarparkRecord> records = new(StringComparer.OrdinalIgnoreCase);

        foreach (JsonElement carparkElement in carparksElement.EnumerateArray())
        {
            if (!TryParseCarpark(carparkElement, out LiveCarparkRecord? record))
            {
                continue;
            }

            records[record!.CarParkNumber] = record;
        }

        if (records.Count == 0)
        {
            throw new InvalidDataException("The live availability payload does not contain any valid carpark records.");
        }

        return new LiveAvailabilitySnapshot(
            records,
            sourceTimestamp ?? records.Values.MaxBy(record => record.UpdateTimeSgt)?.UpdateTimeSgt,
            GetCurrentSingaporeTime(),
            UsingLastKnownGood: false,
            UsingSampleData: usingSampleData,
            WarningMessage: null);
    }

    private bool TryParseCarpark(JsonElement carparkElement, out LiveCarparkRecord? record)
    {
        record = null;

        if (!carparkElement.TryGetProperty("carpark_number", out JsonElement numberElement))
        {
            return false;
        }

        string? rawNumber = numberElement.GetString();
        if (string.IsNullOrWhiteSpace(rawNumber))
        {
            return false;
        }

        if (!carparkElement.TryGetProperty("update_datetime", out JsonElement updateElement))
        {
            return false;
        }

        DateTimeOffset? updateTime = TryParseSingaporeDateTime(updateElement.GetString());
        if (updateTime is null)
        {
            return false;
        }

        if (!carparkElement.TryGetProperty("carpark_info", out JsonElement infoElement) || infoElement.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        Dictionary<string, LotAvailability> availability = new(StringComparer.OrdinalIgnoreCase);

        foreach (JsonElement lotElement in infoElement.EnumerateArray())
        {
            if (!TryParseLotAvailability(lotElement, out string? lotType, out LotAvailability? lotAvailability))
            {
                continue;
            }

            availability[lotType!] = lotAvailability!;
        }

        if (availability.Count == 0)
        {
            return false;
        }

        record = new LiveCarparkRecord(HdbCarparkCatalog.NormalizeCarparkNumber(rawNumber), updateTime.Value, availability);
        return true;
    }

    private static bool TryParseLotAvailability(JsonElement lotElement, out string? lotType, out LotAvailability? availability)
    {
        lotType = null;
        availability = null;

        if (!lotElement.TryGetProperty("lot_type", out JsonElement lotTypeElement)
            || !lotElement.TryGetProperty("total_lots", out JsonElement totalLotsElement)
            || !lotElement.TryGetProperty("lots_available", out JsonElement availableLotsElement))
        {
            return false;
        }

        lotType = lotTypeElement.GetString()?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(lotType) || !SupportedLotTypes.Contains(lotType, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!int.TryParse(totalLotsElement.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int totalLots)
            || !int.TryParse(availableLotsElement.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int availableLots))
        {
            return false;
        }

        double? occupancyRate = totalLots > 0
            ? Math.Clamp((double)(totalLots - availableLots) / totalLots, 0d, 1d)
            : null;

        availability = new LotAvailability(totalLots, availableLots, occupancyRate);
        return true;
    }

    private static DateTimeOffset? TryParseDateTimeOffset(JsonElement element) =>
        element.ValueKind == JsonValueKind.String
            ? TryParseSingaporeDateTime(element.GetString())
            : null;

    public static DateTimeOffset? TryParseSingaporeDateTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset withOffset))
        {
            return TimeZoneInfo.ConvertTime(withOffset, SingaporeTimeZone);
        }

        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime localTime))
        {
            DateTime unspecified = DateTime.SpecifyKind(localTime, DateTimeKind.Unspecified);
            return new DateTimeOffset(unspecified, SingaporeTimeZone.GetUtcOffset(unspecified));
        }

        return null;
    }

    public static DateTimeOffset GetCurrentSingaporeTime() =>
        TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, SingaporeTimeZone);

    private static bool HasConfiguredApiKey(string? apiKey) =>
        !string.IsNullOrWhiteSpace(apiKey)
        && !apiKey.Contains("{{", StringComparison.Ordinal)
        && !apiKey.Contains("DATA_GOV_SG_API_KEY", StringComparison.OrdinalIgnoreCase);

    private string ResolveDataPath(string fileName)
    {
        string[] candidatePaths =
        [
            Path.Combine(AppContext.BaseDirectory, "Data", fileName),
            Path.Combine(_environment.ContentRootPath, "Data", fileName),
            Path.Combine(_environment.ContentRootPath, "..", "..", "data", fileName)
        ];

        return candidatePaths.FirstOrDefault(File.Exists) ?? candidatePaths[0];
    }
}
