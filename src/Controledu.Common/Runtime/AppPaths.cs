namespace Controledu.Common.Runtime;

/// <summary>
/// Shared filesystem path helpers.
/// </summary>
public static class AppPaths
{
    private const string DiagnosticsEnabledFileName = "diagnostics.enabled";

    /// <summary>
    /// Returns base app data path for Controledu.
    /// </summary>
    public static string GetBasePath()
    {
        var candidate = GetMachineBasePath();

        if (TryEnsureWritableDirectory(candidate))
        {
            return candidate;
        }

        var fallback = GetUserBasePath();
        Directory.CreateDirectory(fallback);
        return fallback;
    }

    /// <summary>
    /// Returns the machine-wide Controledu data path without creating it.
    /// </summary>
    public static string GetMachineBasePath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Controledu");

    /// <summary>
    /// Returns the current-user Controledu data path without creating it.
    /// </summary>
    public static string GetUserBasePath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Controledu");

    /// <summary>
    /// Builds a database file path under the shared app path.
    /// </summary>
    public static string GetDatabasePath(string fileName) => Path.Combine(GetBasePath(), fileName);

    /// <summary>
    /// Returns whether diagnostic rolling file logs should be written.
    /// </summary>
    public static bool IsFileLoggingEnabled()
    {
        var environmentValue = Environment.GetEnvironmentVariable("CONTROLEDU_DIAGNOSTIC_LOGS");
        if (TryParseBoolean(environmentValue, out var environmentEnabled))
        {
            return environmentEnabled;
        }

        var markerPath = Path.Combine(GetMachineBasePath(), DiagnosticsEnabledFileName);
        try
        {
            if (File.Exists(markerPath)
                && TryParseBoolean(File.ReadAllText(markerPath).Trim(), out var markerEnabled))
            {
                return markerEnabled;
            }
        }
        catch
        {
            // Keep diagnostics enabled if the deployment marker cannot be read.
        }

        return true;
    }

    /// <summary>
    /// Returns the rolling log directory.
    /// </summary>
    public static string GetLogsPath()
    {
        var logsPath = Path.Combine(GetBasePath(), "logs");
        Directory.CreateDirectory(logsPath);
        return logsPath;
    }

    /// <summary>
    /// Returns dataset root path.
    /// </summary>
    public static string GetDatasetsPath()
    {
        var path = Path.Combine(GetBasePath(), "Datasets");
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// Returns export artifacts path.
    /// </summary>
    public static string GetExportsPath()
    {
        var path = Path.Combine(GetBasePath(), "exports");
        Directory.CreateDirectory(path);
        return path;
    }

    private static bool TryEnsureWritableDirectory(string path)
    {
        try
        {
            Directory.CreateDirectory(path);

            var probePath = Path.Combine(path, $".controledu-write-test-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probePath, string.Empty);
            File.Delete(probePath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryParseBoolean(string? value, out bool result)
    {
        result = false;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim();
        if (bool.TryParse(normalized, out result))
        {
            return true;
        }

        if (string.Equals(normalized, "1", StringComparison.Ordinal)
            || string.Equals(normalized, "yes", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "on", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "enabled", StringComparison.OrdinalIgnoreCase))
        {
            result = true;
            return true;
        }

        if (string.Equals(normalized, "0", StringComparison.Ordinal)
            || string.Equals(normalized, "no", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "off", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "disabled", StringComparison.OrdinalIgnoreCase))
        {
            result = false;
            return true;
        }

        return false;
    }
}
