namespace Controledu.Teacher.Server.Security;

/// <summary>
/// Shared constants for teacher API token authentication.
/// </summary>
public static class TeacherAuthDefaults
{
    /// <summary>
    /// Authentication scheme name for teacher API token.
    /// </summary>
    public const string AuthenticationScheme = "TeacherApiToken";

    /// <summary>
    /// Header used by teacher UI/API clients.
    /// </summary>
    public const string TokenHeaderName = "X-Controledu-TeacherToken";

    /// <summary>
    /// Authorization policy used for teacher-facing API/hub surface.
    /// </summary>
    public const string TeacherPolicy = "TeacherConsolePolicy";
}
