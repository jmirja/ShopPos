namespace ShopPos.Desktop.Labels;

/// <summary>EAN-13 helper. A 12-digit UPC-A code is printed as EAN-13 with a leading 0 (the bars are identical).</summary>
public static class Ean13
{
    // Right-hand codes. Left "L" codes are their complement, left "G" codes are their reverse.
    private static readonly string[] R =
        { "1110010", "1100110", "1101100", "1000010", "1011100", "1001110", "1010000", "1000100", "1001000", "1110100" };

    // Which of the first six digits use L or G, chosen by the very first digit.
    private static readonly string[] Parity =
        { "LLLLLL", "LLGLGG", "LLGGLG", "LLGGGL", "LGLLGG", "LGGLLG", "LGGGLL", "LGLGLG", "LGLGGL", "LGGLGL" };

    /// <summary>Returns a valid 13-digit code, or null with a short explanation in <paramref name="problem"/>.</summary>
    public static string? Normalize(string? code, out string problem)
    {
        problem = string.Empty;
        var c = code?.Trim() ?? string.Empty;

        if (c.Length == 0) { problem = "No barcode"; return null; }
        if (!c.All(char.IsAsciiDigit)) { problem = "Not an EAN-13 / UPC-A number"; return null; }
        if (c.Length == 12) c = "0" + c;
        if (c.Length != 13) { problem = "Not an EAN-13 / UPC-A number"; return null; }
        if (CheckDigit(c[..12]) != c[12] - '0') { problem = "Wrong check digit"; return null; }

        return c;
    }

    public static int CheckDigit(string first12)
    {
        var sum = 0;
        for (var i = 0; i < 12; i++)
        {
            var digit = first12[i] - '0';
            sum += i % 2 == 0 ? digit : digit * 3;
        }
        return (10 - sum % 10) % 10;
    }

    /// <summary>The 95 modules (true = black bar) of a valid 13-digit code.</summary>
    public static bool[] Modules(string ean13)
    {
        var bits = new List<bool>(95);

        void Add(string pattern)
        {
            foreach (var ch in pattern) bits.Add(ch == '1');
        }

        Add("101");                                              // start guard

        var parity = Parity[ean13[0] - '0'];
        for (var i = 1; i <= 6; i++)
        {
            var right = R[ean13[i] - '0'];
            Add(parity[i - 1] == 'L'
                ? new string(right.Select(ch => ch == '1' ? '0' : '1').ToArray())   // L code
                : new string(right.Reverse().ToArray()));                           // G code
        }

        Add("01010");                                            // centre guard

        for (var i = 7; i <= 12; i++)
            Add(R[ean13[i] - '0']);

        Add("101");                                              // end guard
        return bits.ToArray();
    }
}
