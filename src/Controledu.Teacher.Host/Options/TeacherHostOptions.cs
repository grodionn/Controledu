namespace Controledu.Teacher.Host.Options;

/// <summary>
/// Desktop shell options for teacher host.
/// </summary>
public sealed class TeacherHostOptions
{
    /// <summary>
    /// Configuration section name.
    /// </summary>
    public const string SectionName = "TeacherHost";

    /// <summary>
    /// Window title.
    /// </summary>
    public string WindowTitle { get; set; } = "Controledu Teacher";

    /// <summary>
    /// Starts window maximized.
    /// </summary>
    public bool StartMaximized { get; set; }

    /// <summary>
    /// Optional explicit UI URL. When empty, uses teacher server localhost URL.
    /// </summary>
    public string? UiUrl { get; set; }
}
