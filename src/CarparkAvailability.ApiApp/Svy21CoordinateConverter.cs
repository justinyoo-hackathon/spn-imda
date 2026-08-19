using System.Globalization;

namespace CarparkAvailability.ApiApp;

public sealed class Svy21CoordinateConverter
{
    private const double SemiMajorAxis = 6378137d;
    private const double Flattening = 1d / 298.257223563d;
    private const double OriginLatitudeRadians = 1.366666d * Math.PI / 180d;
    private const double OriginLongitudeRadians = 103.833333d * Math.PI / 180d;
    private const double FalseNorthing = 38744.572d;
    private const double FalseEasting = 28001.642d;
    private const double ScaleFactor = 1d;

    private static readonly double EccentricitySquared = (2d * Flattening) - (Flattening * Flattening);
    private static readonly double EccentricityFourth = EccentricitySquared * EccentricitySquared;
    private static readonly double EccentricitySixth = EccentricityFourth * EccentricitySquared;
    private static readonly double PrimeEccentricitySquared = EccentricitySquared / (1d - EccentricitySquared);
    private static readonly double A0 = 1d - (EccentricitySquared / 4d) - (3d * EccentricityFourth / 64d) - (5d * EccentricitySixth / 256d);
    private static readonly double A2 = (3d / 8d) * (EccentricitySquared + (EccentricityFourth / 4d) + (15d * EccentricitySixth / 128d));
    private static readonly double A4 = (15d / 256d) * (EccentricityFourth + (3d * EccentricitySixth / 4d));
    private static readonly double A6 = 35d * EccentricitySixth / 3072d;
    private static readonly double MeridianDistanceAtOrigin = ComputeMeridianDistance(OriginLatitudeRadians);

    public bool TryConvert(double easting, double northing, out double latitude, out double longitude)
    {
        latitude = 0d;
        longitude = 0d;

        if (double.IsNaN(easting) || double.IsInfinity(easting) || double.IsNaN(northing) || double.IsInfinity(northing))
        {
            return false;
        }

        double meridianDistance = MeridianDistanceAtOrigin + ((northing - FalseNorthing) / ScaleFactor);
        double footprintLatitude = meridianDistance / (SemiMajorAxis * A0);
        double e1 = (1d - Math.Sqrt(1d - EccentricitySquared)) / (1d + Math.Sqrt(1d - EccentricitySquared));

        double j1 = (3d * e1 / 2d) - (27d * Math.Pow(e1, 3d) / 32d);
        double j2 = (21d * Math.Pow(e1, 2d) / 16d) - (55d * Math.Pow(e1, 4d) / 32d);
        double j3 = 151d * Math.Pow(e1, 3d) / 96d;
        double j4 = 1097d * Math.Pow(e1, 4d) / 512d;

        footprintLatitude +=
            (j1 * Math.Sin(2d * footprintLatitude)) +
            (j2 * Math.Sin(4d * footprintLatitude)) +
            (j3 * Math.Sin(6d * footprintLatitude)) +
            (j4 * Math.Sin(8d * footprintLatitude));

        double sinFootprint = Math.Sin(footprintLatitude);
        double cosFootprint = Math.Cos(footprintLatitude);
        double tanFootprint = Math.Tan(footprintLatitude);

        double radiusPrimeVertical = SemiMajorAxis / Math.Sqrt(1d - (EccentricitySquared * sinFootprint * sinFootprint));
        double radiusMeridian = SemiMajorAxis * (1d - EccentricitySquared) / Math.Pow(1d - (EccentricitySquared * sinFootprint * sinFootprint), 1.5d);
        double t1 = tanFootprint * tanFootprint;
        double c1 = PrimeEccentricitySquared * cosFootprint * cosFootprint;
        double d = (easting - FalseEasting) / (radiusPrimeVertical * ScaleFactor);

        double latitudeRadians = footprintLatitude -
            ((radiusPrimeVertical * tanFootprint / radiusMeridian) *
             ((d * d / 2d) -
              ((5d + (3d * t1) + (10d * c1) - (4d * c1 * c1) - (9d * PrimeEccentricitySquared)) * Math.Pow(d, 4d) / 24d) +
              ((61d + (90d * t1) + (298d * c1) + (45d * t1 * t1) - (252d * PrimeEccentricitySquared) - (3d * c1 * c1)) * Math.Pow(d, 6d) / 720d)));

        double longitudeRadians = OriginLongitudeRadians +
            ((d -
              ((1d + (2d * t1) + c1) * Math.Pow(d, 3d) / 6d) +
              ((5d - (2d * c1) + (28d * t1) - (3d * c1 * c1) + (8d * PrimeEccentricitySquared) + (24d * t1 * t1)) * Math.Pow(d, 5d) / 120d)) /
             cosFootprint);

        latitude = latitudeRadians * 180d / Math.PI;
        longitude = longitudeRadians * 180d / Math.PI;

        return latitude is >= 1d and <= 2d && longitude is >= 103d and <= 105d;
    }

    public static bool TryParseCoordinate(string value, out double coordinate) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out coordinate);

    private static double ComputeMeridianDistance(double latitudeRadians) =>
        SemiMajorAxis * ((A0 * latitudeRadians) - (A2 * Math.Sin(2d * latitudeRadians)) + (A4 * Math.Sin(4d * latitudeRadians)) - (A6 * Math.Sin(6d * latitudeRadians)));
}
