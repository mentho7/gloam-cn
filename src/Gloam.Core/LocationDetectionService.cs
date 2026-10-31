using System.Net.Http;
using System.Text.Json;

namespace Gloam.Core;

/// <summary>Explicitly requested IP geolocation for the sun schedule.</summary>
public static class LocationDetectionService
{
    // Do not follow a provider redirect onto an unencrypted endpoint.
    private static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(5),
        MaxResponseContentBufferSize = 65536
    };

    public static async Task<(double Latitude, double Longitude)?> DetectAsync()
    {
        var json = await Client.GetStringAsync("https://ipapi.co/json/").ConfigureAwait(false);
        return ParseCoordinates(json);
    }

    internal static (double Latitude, double Longitude)? ParseCoordinates(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            (root.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.False) ||
            !root.TryGetProperty("latitude", out var latitude) ||
            !root.TryGetProperty("longitude", out var longitude) ||
            latitude.ValueKind != JsonValueKind.Number || longitude.ValueKind != JsonValueKind.Number ||
            !latitude.TryGetDouble(out double lat) || !longitude.TryGetDouble(out double lon) ||
            !double.IsFinite(lat) || !double.IsFinite(lon) ||
            lat < -90 || lat > 90 || lon < -180 || lon > 180)
            return null;
        return (lat, lon);
    }
}
