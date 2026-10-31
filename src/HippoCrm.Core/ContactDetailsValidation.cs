using System.Globalization;
using System.Net.Mail;
using System.Text;
using System.Text.RegularExpressions;

namespace HippoCrm.Core;

// Shared by forms and saves. These format checks never perform DNS or network lookups.
public static class ContactDetailsValidation
{
    public static string Email(string strValue)
    {
        strValue = strValue.Trim();
        const string strError = "Enter an email such as name@example.com, without spaces or a display name.";
        if (strValue.Length > 254 || strValue.Any(chrValue => char.IsWhiteSpace(chrValue) || char.IsControl(chrValue)))
            throw new ArgumentException(strError);
        var iAt = strValue.IndexOf('@');
        if (iAt <= 0 || iAt != strValue.LastIndexOf('@')) throw new ArgumentException(strError);
        var strLocal = strValue[..iAt];
        if (Encoding.UTF8.GetByteCount(strLocal) > 64 || strLocal.Split('.').Any(strPart => strPart.Length == 0
            || strPart.Any(chrValue => !char.IsLetterOrDigit(chrValue)
                && char.GetUnicodeCategory(chrValue) != UnicodeCategory.NonSpacingMark
                && !"!#$%&'*+-/=?^_`{|}~".Contains(chrValue))))
            throw new ArgumentException(strError);
        string strDomain;
        try { strDomain = new IdnMapping().GetAscii(strValue[(iAt + 1)..]).ToLowerInvariant(); }
        catch (ArgumentException) { throw new ArgumentException(strError); }
        var arrLabels = strDomain.Split('.');
        if (strDomain.Length > 253 || arrLabels.Length < 2 || arrLabels.Any(strLabel =>
            strLabel.Length is < 1 or > 63 || !char.IsAsciiLetterOrDigit(strLabel[0])
            || !char.IsAsciiLetterOrDigit(strLabel[^1])
            || strLabel.Any(chrValue => !char.IsAsciiLetterOrDigit(chrValue) && chrValue != '-'))
            || arrLabels[^1].Length < 2 || arrLabels[^1].All(char.IsAsciiDigit))
            throw new ArgumentException(strError);
        var strAddress = strLocal + "@" + strDomain;
        if (Encoding.UTF8.GetByteCount(strAddress) > 254 || !MailAddress.TryCreate(strAddress, out var objAddress)
            || objAddress.Address != strAddress) throw new ArgumentException(strError);
        return strAddress;
    }

    public static string Phone(string strValue)
    {
        strValue = strValue.Trim().Replace('\u00a0', ' ').Replace('\u2011', '-').Replace('\u2013', '-');
        const string strError = "Enter 10 US/Canada digits, or +country code and a full international number (7-15 digits). Use ext. followed by 1-6 digits for an extension.";
        if (strValue.Length is < 7 or > 80 || strValue.Any(char.IsControl)) throw new ArgumentException(strError);
        // Normalize decimal digits copied from international keyboards to the dialing alphabet.
        strValue = new string(strValue.Select(chrValue => char.IsDigit(chrValue)
            ? (char)('0' + (int)char.GetNumericValue(chrValue)) : chrValue).ToArray());
        var objExtension = Regex.Match(strValue, @"\s*(?:ext\.?|x|#)\s*([0-9]{1,6})$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        var strExtension = objExtension.Success ? " ext. " + objExtension.Groups[1].Value : "";
        var strNumber = objExtension.Success ? strValue[..objExtension.Index].TrimEnd() : strValue;
        if (strNumber.Length == 0 || strNumber.Any(chrValue => !char.IsAsciiDigit(chrValue) && !"+() -.".Contains(chrValue))
            || strNumber.Count(chrValue => chrValue == '+') > 1 || (strNumber.Contains('+') && strNumber[0] != '+'))
            throw new ArgumentException(strError);
        var iOpen = strNumber.IndexOf('('); var iClose = strNumber.IndexOf(')');
        if ((iOpen >= 0 || iClose >= 0) && (iOpen < 0 || iClose <= iOpen + 1
            || strNumber.Count(chrValue => chrValue == '(') != 1 || strNumber.Count(chrValue => chrValue == ')') != 1
            || strNumber[(iOpen + 1)..iClose].Any(chrValue => !char.IsAsciiDigit(chrValue))))
            throw new ArgumentException(strError);
        var strDigits = new string(strNumber.Where(char.IsAsciiDigit).ToArray());
        if (strNumber.StartsWith('+') && !strNumber.StartsWith("+1", StringComparison.Ordinal))
        {
            if (strDigits.Length is < 7 or > 15 || strDigits[0] == '0') throw new ArgumentException(strError);
            return "+" + strDigits + strExtension;
        }
        if (strDigits.Length == 11 && strDigits[0] == '1') strDigits = strDigits[1..];
        if (strDigits.Length != 10 || strDigits[0] is < '2' or > '9' || strDigits[3] is < '2' or > '9')
            throw new ArgumentException(strError);
        if (iOpen >= 0 && iClose - iOpen != 4) throw new ArgumentException(strError);
        return $"+1 ({strDigits[..3]}) {strDigits[3..6]}-{strDigits[6..]}" + strExtension;
    }

    public static string Emails(string? strValues) => Normalize(strValues, Email);
    public static string Phones(string? strValues) => Normalize(strValues, Phone);
    private static string Normalize(string? strValues, Func<string, string> fnNormalize)
    {
        if (string.IsNullOrWhiteSpace(strValues)) return "";
        if (strValues.Length > 16384) throw new ArgumentException("Keep contact details under 16,384 characters.");
        return string.Join(Environment.NewLine, strValues.Split(['\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(fnNormalize).Distinct(StringComparer.OrdinalIgnoreCase));
    }
}
