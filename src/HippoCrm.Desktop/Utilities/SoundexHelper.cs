namespace HippoCrm.Desktop.Utilities;

public static class SoundexHelper
{
    // Standard English Soundex uses A-Z. Inputs without those letters have no phonetic code.
    public static string GetSoundexCode(string? strText)
    {
        if (string.IsNullOrWhiteSpace(strText)) return "";
        Span<char> arrCode = stackalloc char[4];
        arrCode.Fill('0');
        var iLength = 0;
        var chrPreviousCode = '0';
        foreach (var chrOriginal in strText)
        {
            var chrLetter = char.ToUpperInvariant(chrOriginal);
            if (chrLetter is < 'A' or > 'Z') continue;
            var chrCode = GetConsonantCode(chrLetter);
            if (iLength == 0)
            {
                arrCode[iLength++] = chrLetter;
                chrPreviousCode = chrCode;
                continue;
            }
            // H/W do not separate equal consonant codes; vowels and Y do.
            if (chrLetter is 'H' or 'W') continue;
            if (chrCode != '0' && chrCode != chrPreviousCode)
            {
                arrCode[iLength++] = chrCode;
                if (iLength == arrCode.Length) break;
            }
            chrPreviousCode = chrCode;
        }
        return iLength == 0 ? "" : new string(arrCode);
    }

    private static char GetConsonantCode(char chrLetter) => chrLetter switch
    {
        'B' or 'F' or 'P' or 'V' => '1',
        'C' or 'G' or 'J' or 'K' or 'Q' or 'S' or 'X' or 'Z' => '2',
        'D' or 'T' => '3',
        'L' => '4',
        'M' or 'N' => '5',
        'R' => '6',
        _ => '0'
    };
}
