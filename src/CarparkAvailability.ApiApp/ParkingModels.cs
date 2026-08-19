using System.Text.Json.Serialization;

namespace CarparkAvailability.ApiApp;

public enum FreshnessState
{
    Fresh,
    Stale,
    Unavailable,
    Error
}

public sealed record ParkingSearchRequest(
    double Latitude,
    double Longitude,
    string? DestinationLabel,
    string? LotType,
    bool AvailableOnly,
    bool NightParkingOnly,
    string? CarParkType);

public sealed record DestinationSuggestion(
    string Label,
    string Description,
    double Latitude,
    double Longitude,
    bool IsMock);

public sealed record DataStatusResponse(
    FreshnessState FreshnessState,
    DateTimeOffset? SourceUpdateTimeSgt,
    DateTimeOffset DataRetrievedAtSgt,
    bool UsingLastKnownGood,
    bool UsingSampleData,
    int LoadedStaticCarparks,
    int ExcludedStaticRows,
    int AvailableLiveCarparks,
    string? WarningMessage);

public sealed record ParkingSearchResponse(
    string? DestinationLabel,
    double Latitude,
    double Longitude,
    string LotType,
    bool AvailableOnly,
    bool NightParkingOnly,
    string? CarParkType,
    DataStatusResponse Status,
    IReadOnlyList<CarparkResult> Results);

public sealed record CarparkDetailResponse(
    DataStatusResponse Status,
    CarparkResult Carpark);

public sealed record CarparkResult(
    string CarParkNumber,
    string Address,
    double Latitude,
    double Longitude,
    double? DistanceMeters,
    string CarParkType,
    string ParkingSystem,
    string ShortTermParking,
    string FreeParking,
    string NightParking,
    int? Decks,
    double? GantryHeight,
    bool IsBasement,
    DateTimeOffset? SourceUpdateTimeSgt,
    FreshnessState FreshnessState,
    int? TotalLots,
    int? AvailableLots,
    double? OccupancyRate,
    IReadOnlyDictionary<string, LotAvailability> AvailabilityByLotType);

public sealed record LotAvailability(int TotalLots, int AvailableLots, double? OccupancyRate);

public sealed record HdbCarparkRecord(
    string CarParkNumber,
    string Address,
    double Latitude,
    double Longitude,
    string CarParkType,
    string ParkingSystem,
    string ShortTermParking,
    string FreeParking,
    string NightParking,
    int? Decks,
    double? GantryHeight,
    bool IsBasement);

public sealed record LiveCarparkRecord(
    string CarParkNumber,
    DateTimeOffset UpdateTimeSgt,
    IReadOnlyDictionary<string, LotAvailability> AvailabilityByLotType);

public sealed record LiveAvailabilitySnapshot(
    IReadOnlyDictionary<string, LiveCarparkRecord> Carparks,
    DateTimeOffset? SourceUpdateTimeSgt,
    DateTimeOffset DataRetrievedAtSgt,
    bool UsingLastKnownGood,
    bool UsingSampleData,
    string? WarningMessage);

internal sealed record SearchAlias(string Label, string MatchText, string Description);

[JsonSerializable(typeof(ParkingSearchRequest))]
[JsonSerializable(typeof(ParkingSearchResponse))]
[JsonSerializable(typeof(CarparkDetailResponse))]
[JsonSerializable(typeof(DataStatusResponse))]
[JsonSerializable(typeof(DestinationSuggestion[]))]
internal partial class ParkingJsonContext : JsonSerializerContext;
