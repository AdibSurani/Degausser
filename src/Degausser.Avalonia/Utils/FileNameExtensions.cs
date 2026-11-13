namespace Degausser;

internal static class FileNameExtensions
{
    public static string IOFriendly(this string value)
        => string.Concat(value.Select(c => "<>|:*?/\\\"".Contains(c) ? (char)(c + 0xFEE0) : c));
}
