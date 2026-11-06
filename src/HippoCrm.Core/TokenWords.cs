namespace HippoCrm.Core;

// One grammar for migration, retained saves and consistency inspection. No values escape to diagnostics.
internal static class TokenWords
{
    internal static IEnumerable<string> Parse(string? strText, Action? actInvalid = null)
    {
        var arrParts = (strText ?? "").Split([',', '\r', '\n']);
        var setSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var strPart in arrParts)
            {
                var strWord = strPart.Trim();
                if (strWord.Length == 0) continue;
                if (strWord.Length > 64 || strWord.Any(char.IsControl)) { actInvalid?.Invoke(); continue; }
                if (setSeen.Add(strWord)) yield return strWord;
            }
        }
        finally { setSeen.Clear(); Array.Clear(arrParts); }
    }
}
