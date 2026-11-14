namespace SprocketExpedition.Core.Encounters;

// Content references are local identifiers only. Paths, traversal, whitespace,
// control characters and external asset locations are rejected so a catalogue
// can never point at a raw game asset or an arbitrary filesystem location.
public static class ContentSafety
{
    public const int MaxReferenceLength = 128;
    public const int HashHexLength = 64;

    public static bool IsSafeLocalReference(string? value)
    {
        if (string.IsNullOrEmpty(value)
            || value.Length > MaxReferenceLength
            || value.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        char first = value[0];
        if (!IsAsciiLetterOrDigit(first))
        {
            return false;
        }

        foreach (char c in value)
        {
            bool allowed = IsAsciiLetterOrDigit(c) || c == '.' || c == '_' || c == '-';
            if (!allowed)
            {
                return false;
            }
        }

        return true;
    }

    public static bool IsSafeVersion(string? value)
    {
        if (string.IsNullOrEmpty(value)
            || value.Length > 64
            || value.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        char first = value[0];
        if (!IsAsciiLetterOrDigit(first))
        {
            return false;
        }

        foreach (char c in value)
        {
            bool allowed = IsAsciiLetterOrDigit(c)
                || c == '.'
                || c == '_'
                || c == '-'
                || c == '+';
            if (!allowed)
            {
                return false;
            }
        }

        return true;
    }

    public static bool IsSafeRunId(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 200)
        {
            return false;
        }

        bool hasVisible = false;
        foreach (char c in value)
        {
            if (char.IsControl(c))
            {
                return false;
            }

            if (!char.IsWhiteSpace(c))
            {
                hasVisible = true;
            }
        }

        return hasVisible;
    }

    public static bool IsHexHash(string? value, int expectedLength = HashHexLength)
    {
        if (value is null || value.Length != expectedLength)
        {
            return false;
        }

        foreach (char c in value)
        {
            bool hex = (c >= '0' && c <= '9')
                || (c >= 'a' && c <= 'f')
                || (c >= 'A' && c <= 'F');
            if (!hex)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAsciiLetterOrDigit(char c) =>
        (c >= 'a' && c <= 'z')
        || (c >= 'A' && c <= 'Z')
        || (c >= '0' && c <= '9');
}
