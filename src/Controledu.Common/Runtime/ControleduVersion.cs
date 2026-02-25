using System.Reflection;

namespace Controledu.Common.Runtime;

/// <summary>
/// Provides runtime application version metadata for hosts and tools.
/// </summary>
public static class ControleduVersion
{
    /// <summary>
    /// Returns a human-readable version (for example, <c>0.1.1b</c>).
    /// </summary>
    public static string GetDisplayVersion(Assembly? assembly = null)
    {
        var targetAssembly = assembly ?? Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var informational = targetAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            // SDK may append source metadata after '+'.
            var plusIndex = informational.IndexOf('+', StringComparison.Ordinal);
            return (plusIndex >= 0 ? informational[..plusIndex] : informational).Trim();
        }

        var version = targetAssembly.GetName().Version;
        return version is null
            ? "0.0.0"
            : $"{version.Major}.{Math.Max(0, version.Minor)}.{Math.Max(0, version.Build)}";
    }
}
