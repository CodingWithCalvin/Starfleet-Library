using System.Text;

namespace StarfleetLibrary.Core;

public static class IsbnUtility
{
    /// <summary>Remove formatting separators only; unknown characters remain invalid.</summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var result = new StringBuilder(value.Length);
        foreach (var c in value)
            if (c != '-' && !char.IsWhiteSpace(c)) result.Append(char.ToUpperInvariant(c));
        return result.ToString();
    }

    public static bool IsValid(string? value)
    {
        var clean = Normalize(value);
        if (clean.Length == 10)
        {
            var sum = 0;
            for (var i = 0; i < 10; i++)
            {
                var digit = clean[i] == 'X' && i == 9 ? 10 : clean[i] is >= '0' and <= '9' ? clean[i] - '0' : -1;
                if (digit < 0) return false;
                sum += digit * (10 - i);
            }
            return sum % 11 == 0;
        }
        if (clean.Length != 13 || !(clean.StartsWith("978", StringComparison.Ordinal) || clean.StartsWith("979", StringComparison.Ordinal)))
            return false;
        var checksum = 0;
        for (var i = 0; i < 13; i++)
        {
            if (clean[i] is < '0' or > '9') return false;
            checksum += (clean[i] - '0') * (i % 2 == 0 ? 1 : 3);
        }
        return checksum % 10 == 0;
    }

    /// <summary>Return a valid ISBN-13 comparison key, converting equivalent ISBN-10 values.</summary>
    public static string? Canonical(string? value)
    {
        var clean = Normalize(value);
        if (!IsValid(clean)) return null;
        if (clean.Length == 13) return clean;
        var body = "978" + clean[..9];
        var sum = 0;
        for (var i = 0; i < body.Length; i++) sum += (body[i] - '0') * (i % 2 == 0 ? 1 : 3);
        return body + (char)('0' + (10 - sum % 10) % 10);
    }
}
