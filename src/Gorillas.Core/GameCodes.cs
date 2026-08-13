using Gorillas.Core.Primitives;

namespace Gorillas.Core;

/// <summary>
/// Short, speakable join codes. Ambiguous characters (0/O, 1/I) are excluded so a code can be
/// read aloud over a call or typed without confusion.
/// </summary>
public static class GameCodes
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public static string Generate(IRandomSource random)
    {
        var characters = new char[7];

        for (var i = 0; i < 3; i++)
        {
            characters[i] = Alphabet[random.NextInt(0, Alphabet.Length)];
        }

        characters[3] = '-';

        for (var i = 4; i < characters.Length; i++)
        {
            characters[i] = Alphabet[random.NextInt(0, Alphabet.Length)];
        }

        return new string(characters);
    }

    /// <summary>Accepts sloppy input: lower case, missing dash, surrounding whitespace.</summary>
    public static string Normalize(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return string.Empty;
        }

        var kept = new string([.. code.ToUpperInvariant().Where(Alphabet.Contains)]);

        return kept.Length <= 3 ? kept : $"{kept[..3]}-{kept[3..]}";
    }
}
