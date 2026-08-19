namespace CarparkAvailability.ApiApp;

public sealed class ParkingSearchService
{
    private readonly HdbCarparkCatalog _catalog;
    private readonly LiveAvailabilityClient _liveAvailabilityClient;

    public ParkingSearchService(HdbCarparkCatalog catalog, LiveAvailabilityClient liveAvailabilityClient)
    {
        _catalog = catalog;
        _liveAvailabilityClient = liveAvailabilityClient;
    }

    public IReadOnlyList<DestinationSuggestion> SearchDestinations(string query, int limit = 8) =>
        _catalog.SearchDestinations(query, limit);

    public async Task<DataStatusResponse> GetStatusAsync(CancellationToken cancellationToken)
    {
        LiveAvailabilitySnapshot snapshot = await _liveAvailabilityClient.GetSnapshotAsync(cancellationToken);
        return CreateStatus(snapshot);
    }

    public async Task<ParkingSearchResponse> SearchAsync(ParkingSearchRequest request, CancellationToken cancellationToken)
    {
        LiveAvailabilitySnapshot snapshot = await _liveAvailabilityClient.GetSnapshotAsync(cancellationToken);
        string selectedLotType = NormalizeLotType(request.LotType);
        DataStatusResponse status = CreateStatus(snapshot);

        IReadOnlyList<CarparkResult> results = _catalog.Records
            .Select(record => BuildResult(record, snapshot, request.Latitude, request.Longitude, selectedLotType))
            .OfType<CarparkResult>()
            .Where(result => result.DistanceMeters is <= 500d)
            .Where(result => MatchesFilters(result, request, selectedLotType))
            .OrderBy(result => result.DistanceMeters)
            .ThenByDescending(result => result.AvailableLots ?? -1)
            .ThenBy(result => result.OccupancyRate ?? double.MaxValue)
            .ThenBy(result => result.CarParkNumber, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new ParkingSearchResponse(
            request.DestinationLabel,
            request.Latitude,
            request.Longitude,
            selectedLotType,
            request.AvailableOnly,
            request.NightParkingOnly,
            request.CarParkType,
            status,
            results);
    }

    public async Task<CarparkDetailResponse?> GetDetailAsync(string carparkNumber, CancellationToken cancellationToken)
    {
        HdbCarparkRecord? record = _catalog.FindByCarparkNumber(carparkNumber);
        if (record is null)
        {
            return null;
        }

        LiveAvailabilitySnapshot snapshot = await _liveAvailabilityClient.GetSnapshotAsync(cancellationToken);
        DataStatusResponse status = CreateStatus(snapshot);

        return new CarparkDetailResponse(
            status,
            BuildResult(record, snapshot, destinationLatitude: null, destinationLongitude: null, selectedLotType: "C")!);
    }

    public IReadOnlyCollection<string> GetCarParkTypes() => _catalog.CarParkTypes;

    public static FreshnessState ClassifyFreshness(DateTimeOffset? sourceUpdateTimeSgt, bool hasAvailability)
    {
        if (!hasAvailability || sourceUpdateTimeSgt is null)
        {
            return FreshnessState.Unavailable;
        }

        TimeSpan age = LiveAvailabilityClient.GetCurrentSingaporeTime() - sourceUpdateTimeSgt.Value;
        return age <= TimeSpan.FromMinutes(2)
            ? FreshnessState.Fresh
            : FreshnessState.Stale;
    }

    public static double CalculateDistanceMeters(double originLatitude, double originLongitude, double destinationLatitude, double destinationLongitude)
    {
        const double earthRadius = 6371000d;
        double dLat = ToRadians(destinationLatitude - originLatitude);
        double dLon = ToRadians(destinationLongitude - originLongitude);
        double lat1 = ToRadians(originLatitude);
        double lat2 = ToRadians(destinationLatitude);

        double a = Math.Sin(dLat / 2d) * Math.Sin(dLat / 2d)
                   + (Math.Cos(lat1) * Math.Cos(lat2) * Math.Sin(dLon / 2d) * Math.Sin(dLon / 2d));

        double c = 2d * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1d - a));
        return earthRadius * c;
    }

    private DataStatusResponse CreateStatus(LiveAvailabilitySnapshot snapshot) =>
        new(
            ClassifyFreshness(snapshot.SourceUpdateTimeSgt, snapshot.Carparks.Count > 0),
            snapshot.SourceUpdateTimeSgt,
            snapshot.DataRetrievedAtSgt,
            snapshot.UsingLastKnownGood,
            snapshot.UsingSampleData,
            _catalog.LoadedCount,
            _catalog.ExcludedRowCount,
            snapshot.Carparks.Count,
            snapshot.WarningMessage);

    private static CarparkResult? BuildResult(
        HdbCarparkRecord record,
        LiveAvailabilitySnapshot snapshot,
        double? destinationLatitude,
        double? destinationLongitude,
        string selectedLotType)
    {
        snapshot.Carparks.TryGetValue(record.CarParkNumber, out LiveCarparkRecord? liveRecord);
        IReadOnlyDictionary<string, LotAvailability> availability = liveRecord?.AvailabilityByLotType ?? new Dictionary<string, LotAvailability>();

        availability.TryGetValue(selectedLotType, out LotAvailability? selectedAvailability);

        bool hasAvailability = availability.Count > 0;
        FreshnessState freshness = ClassifyFreshness(liveRecord?.UpdateTimeSgt ?? snapshot.SourceUpdateTimeSgt, hasAvailability);
        double? distance = destinationLatitude.HasValue && destinationLongitude.HasValue
            ? CalculateDistanceMeters(destinationLatitude.Value, destinationLongitude.Value, record.Latitude, record.Longitude)
            : null;

        return new CarparkResult(
            record.CarParkNumber,
            record.Address,
            record.Latitude,
            record.Longitude,
            distance is null ? null : Math.Round(distance.Value, 1),
            record.CarParkType,
            record.ParkingSystem,
            record.ShortTermParking,
            record.FreeParking,
            record.NightParking,
            record.Decks,
            record.GantryHeight,
            record.IsBasement,
            liveRecord?.UpdateTimeSgt ?? snapshot.SourceUpdateTimeSgt,
            freshness,
            selectedAvailability?.TotalLots,
            selectedAvailability?.AvailableLots,
            selectedAvailability?.OccupancyRate,
            availability);
    }

    private static bool MatchesFilters(CarparkResult result, ParkingSearchRequest request, string selectedLotType)
    {
        if (request.AvailableOnly && (result.AvailableLots ?? 0) <= 0)
        {
            return false;
        }

        if (request.NightParkingOnly && !string.Equals(result.NightParking, "YES", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(request.CarParkType)
            && !string.Equals(result.CarParkType, request.CarParkType, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return result.AvailabilityByLotType.ContainsKey(selectedLotType) || result.AvailabilityByLotType.Count == 0;
    }

    private static string NormalizeLotType(string? lotType)
    {
        string normalized = string.IsNullOrWhiteSpace(lotType) ? "C" : lotType.Trim().ToUpperInvariant();
        return normalized is "C" or "H" or "S" or "Y" ? normalized : "C";
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180d;
}
