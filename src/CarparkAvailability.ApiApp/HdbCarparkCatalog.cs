using System.Globalization;
using Microsoft.VisualBasic.FileIO;

namespace CarparkAvailability.ApiApp;

public sealed class HdbCarparkCatalog
{
    private static readonly SearchAlias[] Aliases =
    [
        new("Bugis", "ALBERT CENTRE", "Mock destination near Bugis using HDB parking data"),
        new("Toa Payoh", "TOA PAYOH", "Mock destination near Toa Payoh"),
        new("Tampines", "TAMPINES", "Mock destination near Tampines"),
        new("Jurong East", "JURONG EAST", "Mock destination near Jurong East"),
        new("Ang Mo Kio", "ANG MO KIO", "Mock destination near Ang Mo Kio"),
        new("Chinatown", "SMITH STREET", "Mock destination near Chinatown")
    ];

    private readonly ILogger<HdbCarparkCatalog> _logger;
    private readonly List<HdbCarparkRecord> _records = [];
    private readonly Dictionary<string, HdbCarparkRecord> _recordsByNumber = new(StringComparer.OrdinalIgnoreCase);
    private readonly IReadOnlyList<DestinationSuggestion> _aliasSuggestions;

    public HdbCarparkCatalog(IHostEnvironment environment, Svy21CoordinateConverter converter, ILogger<HdbCarparkCatalog> logger)
    {
        _logger = logger;

        string csvPath = ResolveDataPath(environment.ContentRootPath, "HDBCarparkInformation.csv");
        if (!File.Exists(csvPath))
        {
            throw new FileNotFoundException("The HDB carpark dataset was not found.", csvPath);
        }

        ExcludedRowCount = Load(csvPath, converter);
        LoadedCount = _records.Count;
        _aliasSuggestions = BuildAliasSuggestions();

        _logger.LogInformation("Loaded {LoadedCount} HDB carparks and excluded {ExcludedRowCount} malformed rows.", LoadedCount, ExcludedRowCount);
    }

    public int LoadedCount { get; }

    public int ExcludedRowCount { get; }

    public IReadOnlyList<HdbCarparkRecord> Records => _records;

    public IReadOnlyCollection<string> CarParkTypes =>
        _records.Select(record => record.CarParkType)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public HdbCarparkRecord? FindByCarparkNumber(string carparkNumber)
    {
        _recordsByNumber.TryGetValue(NormalizeCarparkNumber(carparkNumber), out HdbCarparkRecord? record);
        return record;
    }

    public IReadOnlyList<DestinationSuggestion> SearchDestinations(string query, int limit = 8)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        string normalized = query.Trim();

        List<DestinationSuggestion> results =
        [
            .. _aliasSuggestions.Where(alias =>
                alias.Label.Contains(normalized, StringComparison.OrdinalIgnoreCase)
                || alias.Description.Contains(normalized, StringComparison.OrdinalIgnoreCase)),
            .. _records
                .Where(record => record.Address.Contains(normalized, StringComparison.OrdinalIgnoreCase))
                .Select(record => new DestinationSuggestion(
                    record.Address,
                    $"HDB car park address ({record.CarParkNumber})",
                    record.Latitude,
                    record.Longitude,
                    true))
        ];

        return results
            .DistinctBy(result => result.Label, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .ToArray();
    }

    public static string NormalizeCarparkNumber(string value) => value.Trim().ToUpperInvariant();

    private int Load(string csvPath, Svy21CoordinateConverter converter)
    {
        int excludedRows = 0;

        using TextFieldParser parser = new(csvPath);
        parser.TextFieldType = FieldType.Delimited;
        parser.SetDelimiters(",");
        parser.HasFieldsEnclosedInQuotes = true;

        string[]? headers = parser.ReadFields();
        if (headers is null)
        {
            throw new InvalidOperationException("The HDB carpark dataset is empty.");
        }

        Dictionary<string, int> indexes = headers
            .Select((header, index) => new { header, index })
            .ToDictionary(item => item.header, item => item.index, StringComparer.OrdinalIgnoreCase);

        string[] requiredHeaders =
        [
            "car_park_no", "address", "x_coord", "y_coord", "car_park_type",
            "type_of_parking_system", "short_term_parking", "free_parking",
            "night_parking", "car_park_decks", "gantry_height", "car_park_basement"
        ];

        foreach (string requiredHeader in requiredHeaders)
        {
            if (!indexes.ContainsKey(requiredHeader))
            {
                throw new InvalidOperationException($"The HDB carpark dataset is missing required header '{requiredHeader}'.");
            }
        }

        while (!parser.EndOfData)
        {
            string[]? fields;

            try
            {
                fields = parser.ReadFields();
            }
            catch (MalformedLineException ex)
            {
                excludedRows++;
                _logger.LogWarning(ex, "Excluded malformed CSV line {LineNumber}.", ex.LineNumber);
                continue;
            }

            if (fields is null)
            {
                continue;
            }

            try
            {
                string number = NormalizeCarparkNumber(GetRequiredField(fields, indexes, "car_park_no"));
                if (string.IsNullOrWhiteSpace(number))
                {
                    excludedRows++;
                    continue;
                }

                if (!Svy21CoordinateConverter.TryParseCoordinate(GetRequiredField(fields, indexes, "x_coord"), out double easting)
                    || !Svy21CoordinateConverter.TryParseCoordinate(GetRequiredField(fields, indexes, "y_coord"), out double northing)
                    || !converter.TryConvert(easting, northing, out double latitude, out double longitude))
                {
                    excludedRows++;
                    continue;
                }

                HdbCarparkRecord record = new(
                    number,
                    GetRequiredField(fields, indexes, "address"),
                    latitude,
                    longitude,
                    GetRequiredField(fields, indexes, "car_park_type"),
                    GetRequiredField(fields, indexes, "type_of_parking_system"),
                    GetRequiredField(fields, indexes, "short_term_parking"),
                    GetRequiredField(fields, indexes, "free_parking"),
                    GetRequiredField(fields, indexes, "night_parking"),
                    TryParseInt(fields[indexes["car_park_decks"]]),
                    TryParseDouble(fields[indexes["gantry_height"]]),
                    string.Equals(fields[indexes["car_park_basement"]], "Y", StringComparison.OrdinalIgnoreCase));

                _records.Add(record);
                _recordsByNumber[number] = record;
            }
            catch (Exception ex) when (ex is FormatException or IndexOutOfRangeException)
            {
                excludedRows++;
                _logger.LogWarning(ex, "Excluded malformed HDB carpark row.");
            }
        }

        return excludedRows;
    }

    private IReadOnlyList<DestinationSuggestion> BuildAliasSuggestions() =>
        Aliases.Select(alias =>
            {
                HdbCarparkRecord? match = _records.FirstOrDefault(record =>
                    record.Address.Contains(alias.MatchText, StringComparison.OrdinalIgnoreCase));

                return match is null
                    ? null
                    : new DestinationSuggestion(alias.Label, alias.Description, match.Latitude, match.Longitude, true);
            })
            .OfType<DestinationSuggestion>()
            .ToArray();

    private static string GetRequiredField(string[] fields, IReadOnlyDictionary<string, int> indexes, string key)
    {
        string? value = fields[indexes[key]]?.Trim();
        return string.IsNullOrWhiteSpace(value)
            ? throw new FormatException($"Required field '{key}' is missing.")
            : value;
    }

    private static int? TryParseInt(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : null;

    private static double? TryParseDouble(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
            ? parsed
            : null;

    private static string ResolveDataPath(string contentRootPath, string fileName)
    {
        string[] candidatePaths =
        [
            Path.Combine(AppContext.BaseDirectory, "Data", fileName),
            Path.Combine(contentRootPath, "Data", fileName),
            Path.Combine(contentRootPath, "..", "..", "data", fileName)
        ];

        return candidatePaths.FirstOrDefault(File.Exists) ?? candidatePaths[0];
    }
}
