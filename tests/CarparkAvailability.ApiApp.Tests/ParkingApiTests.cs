using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CarparkAvailability.ApiApp;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CarparkAvailability.ApiApp.Tests;

public sealed class ParkingApiTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
    private static string ApiProjectRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "CarparkAvailability.ApiApp"));

    [Fact]
    public void CoordinateConverter_ConvertsSvy21Origin()
    {
        Svy21CoordinateConverter converter = new();

        bool converted = converter.TryConvert(28001.642d, 38744.572d, out double latitude, out double longitude);

        Assert.True(converted);
        Assert.InRange(latitude, 1.3665d, 1.3668d);
        Assert.InRange(longitude, 103.8331d, 103.8335d);
    }

    [Fact]
    public void CoordinateConverter_ConvertsAlbertCentrePoint()
    {
        Svy21CoordinateConverter converter = new();

        bool converted = converter.TryConvert(30314.7936d, 31490.4942d, out double latitude, out double longitude);

        Assert.True(converted);
        Assert.InRange(latitude, 1.3009d, 1.3012d);
        Assert.InRange(longitude, 103.8539d, 103.8543d);
    }

    [Fact]
    public void DestinationSearch_ReturnsBugisAlias()
    {
        HdbCarparkCatalog catalog = CreateCatalog();

        IReadOnlyList<DestinationSuggestion> suggestions = catalog.SearchDestinations("Bugis");

        Assert.Contains(suggestions, suggestion => string.Equals(suggestion.Label, "Bugis", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FreshnessClassifier_UsesTwoMinuteThreshold()
    {
        DateTimeOffset now = LiveAvailabilityClient.GetCurrentSingaporeTime();

        Assert.Equal(FreshnessState.Fresh, ParkingSearchService.ClassifyFreshness(now.AddMinutes(-1), hasAvailability: true));
        Assert.Equal(FreshnessState.Stale, ParkingSearchService.ClassifyFreshness(now.AddMinutes(-3), hasAvailability: true));
        Assert.Equal(FreshnessState.Unavailable, ParkingSearchService.ClassifyFreshness(null, hasAvailability: false));
    }

    [Fact]
    public async Task StatusEndpoint_UsesSampleFallbackWithoutApiKey()
    {
        await using TestApplicationFactory factory = new();
        HttpClient client = factory.CreateClient();

        DataStatusResponse? status = await client.GetFromJsonAsync<DataStatusResponse>("/api/status", JsonOptions);

        Assert.NotNull(status);
        Assert.True(status!.UsingSampleData);
        Assert.Equal(FreshnessState.Stale, status.FreshnessState);
        Assert.True(status.AvailableLiveCarparks > 0);
    }

    [Fact]
    public async Task SearchEndpoint_ReturnsNearbyBugisCarparks()
    {
        await using TestApplicationFactory factory = new();
        HttpClient client = factory.CreateClient();

        ParkingSearchResponse? response = await (await client.PostAsJsonAsync("/api/search", new ParkingSearchRequest(
            1.3014d,
            103.8545d,
            "Bugis",
            "C",
            false,
            false,
            null))).Content.ReadFromJsonAsync<ParkingSearchResponse>(JsonOptions);

        Assert.NotNull(response);
        Assert.NotEmpty(response!.Results);
        Assert.Contains(response.Results, result => result.CarParkNumber == "ACB");
        Assert.All(response.Results, result => Assert.True((result.DistanceMeters ?? 501d) <= 500d));
    }

    [Fact]
    public async Task SearchEndpoint_RejectsInvalidCoordinates()
    {
        await using TestApplicationFactory factory = new();
        HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/search", new ParkingSearchRequest(
            5d,
            103.8545d,
            "Invalid",
            "C",
            false,
            false,
            null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DetailEndpoint_ReturnsCarparkDetails()
    {
        await using TestApplicationFactory factory = new();
        HttpClient client = factory.CreateClient();

        CarparkDetailResponse? detail = await client.GetFromJsonAsync<CarparkDetailResponse>("/api/carparks/ACB", JsonOptions);

        Assert.NotNull(detail);
        Assert.Equal("ACB", detail!.Carpark.CarParkNumber);
        Assert.Equal(FreshnessState.Stale, detail.Carpark.FreshnessState);
    }

    private static HdbCarparkCatalog CreateCatalog() =>
        new(new TestHostEnvironment(), new Svy21CoordinateConverter(), NullLogger<HdbCarparkCatalog>.Instance);

    private sealed class TestApplicationFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseContentRoot(ApiProjectRoot);
            builder.ConfigureAppConfiguration((_, configurationBuilder) =>
            {
                configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["DataGovSg:ApiKey"] = "{{DATA_GOV_SG_API_KEY}}"
                });
            });
        }
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        private readonly string _contentRootPath = ApiProjectRoot;

        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "CarparkAvailability.ApiApp.Tests";
        public string ContentRootPath { get => _contentRootPath; set { } }
        public IFileProvider ContentRootFileProvider { get; set; }

        public TestHostEnvironment()
        {
            ContentRootFileProvider = new PhysicalFileProvider(_contentRootPath);
        }
    }
}
