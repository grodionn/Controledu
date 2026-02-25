namespace Controledu.Common.Runtime;

/// <summary>
/// Shared filesystem path helpers.
/// </summary>
public static class AppPaths
{
    /// <summary>
    /// Returns base app data path for Controledu.
    /// </summary>
    public static string GetBasePath()
    {
        var candidate = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Controledu");

        try
        {
            Directory.CreateDirectory(candidate);
            return candidate;
        }
        catch
        {
            var fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Controledu");
            Directory.CreateDirectory(fallback);
            return fallback;
        }
    }

    /// <summary>
    /// Builds a database file path under the shared app path.
    /// </summary>
    public static string GetDatabasePath(string fileName) => Path.Combine(GetBasePath(), fileName);

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
}
