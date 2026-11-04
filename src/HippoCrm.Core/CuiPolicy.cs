namespace HippoCrm.Core;

public static class CuiPolicy
{
    public const string strMarking = "CUI";
    public const string strBanner = "CUI — CONTROLLED UNCLASSIFIED INFORMATION";

    internal static void ValidateMarking(string? strClassification)
    {
        if (!string.Equals(strClassification, strMarking, StringComparison.Ordinal))
            throw new ArgumentException("A valid CUI marking is required.");
    }
}
