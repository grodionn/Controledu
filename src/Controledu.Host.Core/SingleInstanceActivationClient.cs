using System.Globalization;

namespace Controledu.Host.Core;

/// <summary>
/// Shared helper for activating an already running desktop host instance.
/// </summary>
public static class SingleInstanceActivationClient
{
    /// <summary>
    /// Attempts to call local host activation endpoint.
    /// </summary>
    public static bool TryActivateWindow(int localPort, TimeSpan? timeout = null)
    {
        try
        {
            using var http = new HttpClient
            {
                Timeout = timeout ?? TimeSpan.FromSeconds(2),
            };

            var url = string.Create(
                CultureInfo.InvariantCulture,
                $"http://127.0.0.1:{localPort}/api/window/show");
            var response = http.PostAsync(url, content: null)
                .GetAwaiter()
                .GetResult();

            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
