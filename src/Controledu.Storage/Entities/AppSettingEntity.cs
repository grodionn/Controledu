namespace Controledu.Storage.Entities;

/// <summary>
/// Key/value configuration row.
/// </summary>
public sealed class AppSettingEntity
{
    /// <summary>
    /// Setting key.
    /// </summary>
    public required string Key { get; set; }

    /// <summary>
    /// Setting value.
    /// </summary>
    public required string Value { get; set; }
}
