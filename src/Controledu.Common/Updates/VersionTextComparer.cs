using System.Text.RegularExpressions;

namespace Controledu.Common.Updates;

/// <summary>
/// Compares Controledu display versions like <c>0.1.1b</c>.
/// </summary>
public sealed partial class VersionTextComparer : IComparer<string>
{
    public static VersionTextComparer Instance { get; } = new();

    public int Compare(string? x, string? y)
    {
        var left = Parse(x);
        var right = Parse(y);

        for (var i = 0; i < 3; i++)
        {
            var cmp = left.Numeric[i].CompareTo(right.Numeric[i]);
            if (cmp != 0)
            {
                return cmp;
            }
        }

        if (string.Equals(left.Suffix, right.Suffix, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (string.IsNullOrEmpty(left.Suffix))
        {
            return 1;
        }

        if (string.IsNullOrEmpty(right.Suffix))
        {
            return -1;
        }

        return string.Compare(left.Suffix, right.Suffix, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsNewer(string currentVersion, string candidateVersion) =>
        Instance.Compare(currentVersion, candidateVersion) < 0;

    private static ParsedVersion Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new ParsedVersion([0, 0, 0], string.Empty);
        }

        var normalized = value.Trim();
        var match = VersionRegex().Match(normalized);
        if (!match.Success)
        {
            return new ParsedVersion([0, 0, 0], normalized);
        }

        var numbers = new int[3];
        numbers[0] = ParseInt(match.Groups["maj"].Value);
        numbers[1] = ParseInt(match.Groups["min"].Value);
        numbers[2] = ParseInt(match.Groups["pat"].Value);

        return new ParsedVersion(numbers, match.Groups["suffix"].Value.Trim());
    }

    private static int ParseInt(string value) =>
        int.TryParse(value, out var parsed) ? parsed : 0;

    private sealed record ParsedVersion(int[] Numeric, string Suffix);

    [GeneratedRegex(@"^\s*(?<maj>\d+)(?:\.(?<min>\d+))?(?:\.(?<pat>\d+))?(?<suffix>[A-Za-z][A-Za-z0-9._-]*)?\s*$", RegexOptions.Compiled)]
    private static partial Regex VersionRegex();
}
