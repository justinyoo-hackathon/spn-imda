using System.Text.Json;
using System.Text.Json.Serialization;
using CarparkAvailability.ApiApp;
using Microsoft.AspNetCore.Mvc;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, ParkingJsonContext.Default);
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
});

builder.Services.AddSingleton<Svy21CoordinateConverter>();
builder.Services.AddSingleton<HdbCarparkCatalog>();
builder.Services.AddSingleton<ParkingSearchService>();
builder.Services.AddHttpClient<LiveAvailabilityClient>(client =>
{
    client.BaseAddress = new Uri("https://api.data.gov.sg");
    client.Timeout = TimeSpan.FromSeconds(10);
});

WebApplication app = builder.Build();

app.UseExceptionHandler();
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/api", () => Results.Ok(new
{
    name = "Smart Parking Navigator API",
    endpoints = new[]
    {
        "/api/destinations?query={text}",
        "/api/search",
        "/api/carparks/{carparkNumber}",
        "/api/status"
    }
}));

app.MapGet("/api/destinations", (string? query, ParkingSearchService searchService) =>
{
    if (string.IsNullOrWhiteSpace(query))
    {
        return Results.Ok(Array.Empty<DestinationSuggestion>());
    }

    return Results.Ok(searchService.SearchDestinations(query));
});

app.MapGet("/api/status", async (ParkingSearchService searchService, CancellationToken cancellationToken) =>
{
    DataStatusResponse status = await searchService.GetStatusAsync(cancellationToken);
    return Results.Ok(status);
});

app.MapGet("/api/carparks/{carparkNumber}", async (string carparkNumber, ParkingSearchService searchService, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(carparkNumber))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["carparkNumber"] = ["A car park number is required."]
        });
    }

    CarparkDetailResponse? detail = await searchService.GetDetailAsync(carparkNumber, cancellationToken);
    return detail is null ? Results.NotFound() : Results.Ok(detail);
});

app.MapPost("/api/search", async (ParkingSearchRequest request, ParkingSearchService searchService, CancellationToken cancellationToken) =>
{
    Dictionary<string, string[]> errors = ValidateRequest(request);
    if (errors.Count > 0)
    {
        return Results.ValidationProblem(errors);
    }

    ParkingSearchResponse response = await searchService.SearchAsync(request, cancellationToken);
    return Results.Ok(response);
});

app.MapDefaultEndpoints();

app.Run();

static Dictionary<string, string[]> ValidateRequest(ParkingSearchRequest request)
{
    Dictionary<string, string[]> errors = [];

    if (request.Latitude is < 1d or > 2d)
    {
        errors["latitude"] = ["Latitude must be within Singapore bounds."];
    }

    if (request.Longitude is < 103d or > 105d)
    {
        errors["longitude"] = ["Longitude must be within Singapore bounds."];
    }

    if (!string.IsNullOrWhiteSpace(request.CarParkType) && request.CarParkType.Length > 120)
    {
        errors["carParkType"] = ["Car park type is too long."];
    }

    if (!string.IsNullOrWhiteSpace(request.DestinationLabel) && request.DestinationLabel.Length > 200)
    {
        errors["destinationLabel"] = ["Destination label is too long."];
    }

    return errors;
}

public partial class Program;
