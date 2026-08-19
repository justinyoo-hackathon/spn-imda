using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CarparkAvailability.WebApp;

public sealed class ParkingApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly HttpClient _httpClient;

    public ParkingApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<DestinationSuggestion>> SearchDestinationsAsync(string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        return await _httpClient.GetFromJsonAsync<List<DestinationSuggestion>>(
                   $"/api/destinations?query={Uri.EscapeDataString(query)}",
                   JsonOptions,
                   cancellationToken)
               ?? [];
    }

    public async Task<DataStatusResponse?> GetStatusAsync(CancellationToken cancellationToken = default) =>
        await _httpClient.GetFromJsonAsync<DataStatusResponse>("/api/status", JsonOptions, cancellationToken);

    public async Task<ParkingSearchResponse> SearchAsync(ParkingSearchRequest request, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync("/api/search", request, JsonOptions, cancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ParkingSearchResponse>(JsonOptions, cancellationToken))!;
    }

    public async Task<CarparkDetailResponse?> GetDetailAsync(string carparkNumber, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.GetAsync($"/api/carparks/{Uri.EscapeDataString(carparkNumber)}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CarparkDetailResponse>(JsonOptions, cancellationToken);
    }
}
