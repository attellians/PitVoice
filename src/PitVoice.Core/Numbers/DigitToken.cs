namespace PitVoice.Core.Numbers;

/// <summary>Shared handling of numbers written in digits (a recognizer or a user may produce "40").</summary>
internal static class DigitToken
{
    /// <summary>Parses "0".."999" (no sign, no separators). Anything else, including "1000", is not a number.</summary>
    public static bool TryParse(string word, out int value)
    {
        value = 0;
        if (word.Length is 0 or > 3)
        {
            return false;
        }

        foreach (var c in word)
        {
            if (c is < '0' or > '9')
            {
                return false;
            }

            value = value * 10 + (c - '0');
        }

        return true;
    }
}
