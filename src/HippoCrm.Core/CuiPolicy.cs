using System.Globalization;
using Microsoft.Data.Sqlite;

namespace HippoCrm.Core;

public enum CuiRecipientKind { Unverified, UsGovernment, UsGovernmentContractor, DoD, UsDodContractor }

// Distribution letters are independent designations, never an ordered clearance.
public sealed record CuiDesignation(string strCategory, string strDistributionStatement = "",
    string strControllingOffice = "", string strDeterminationDate = "", string strDistributionNotice = "");

public abstract class CuiProtectedRecord
{
    // Retained for compatibility with the original database CHECK constraint.
    public string strClassification { get; set; } = CuiPolicy.strMarking;
    public string strCuiCategory { get; set; } = CuiPolicy.strMarking;
    public string strDistributionStatement { get; set; } = "";
    public string strControllingOffice { get; set; } = "";
    public string strDeterminationDate { get; set; } = "";
    public string strDistributionNotice { get; set; } = "";
    public CuiDesignation objCuiDesignation => new(strCuiCategory, strDistributionStatement,
        strControllingOffice, strDeterminationDate, strDistributionNotice);
    public string strCuiBanner => strCuiCategory == CuiPolicy.strMarking ? CuiPolicy.strBanner
        : $"{strCuiCategory} — Distribution Statement {strDistributionStatement}\n{strDistributionNotice}";
}

public static class CuiPolicy
{
    public const string strMarking = "CUI";
    public const string strBanner = "CUI — CONTROLLED UNCLASSIFIED INFORMATION";
    public const string strCtiMarking = "CUI//SP-CTI";
    internal static readonly string[] arrPolicyProperties = [nameof(CuiProtectedRecord.strCuiCategory),
        nameof(CuiProtectedRecord.strDistributionStatement), nameof(CuiProtectedRecord.strControllingOffice),
        nameof(CuiProtectedRecord.strDeterminationDate), nameof(CuiProtectedRecord.strDistributionNotice)];

    internal static string PolicyColumn(string strProperty) => strProperty switch
    {
        nameof(CuiProtectedRecord.strCuiCategory) => "cui_category",
        nameof(CuiProtectedRecord.strDistributionStatement) => "distribution_statement",
        nameof(CuiProtectedRecord.strControllingOffice) => "controlling_office",
        nameof(CuiProtectedRecord.strDeterminationDate) => "determination_date",
        nameof(CuiProtectedRecord.strDistributionNotice) => "distribution_notice",
        _ => throw new ArgumentException("Unknown CUI policy field.")
    };

    public static void Validate(CuiDesignation objDesignation)
    {
        ArgumentNullException.ThrowIfNull(objDesignation);
        ValidatePair(objDesignation.strCategory, objDesignation.strDistributionStatement);
        if (objDesignation.strCategory == strMarking)
        {
            if (objDesignation.strControllingOffice != "" || objDesignation.strDeterminationDate != "" || objDesignation.strDistributionNotice != "")
                throw new ArgumentException("Distribution details require a CTI designation.");
            return;
        }
        if (string.IsNullOrWhiteSpace(objDesignation.strControllingOffice) || objDesignation.strControllingOffice.Length > 256
            || objDesignation.strControllingOffice.Any(char.IsControl)
            || !DateOnly.TryParseExact(objDesignation.strDeterminationDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
            || string.IsNullOrWhiteSpace(objDesignation.strDistributionNotice) || objDesignation.strDistributionNotice.Length is < 26 or > 4096
            || objDesignation.strDistributionNotice.Any(chrValue => char.IsControl(chrValue) && chrValue is not ('\r' or '\n'))
            || !objDesignation.strDistributionNotice.StartsWith($"Distribution Statement {objDesignation.strDistributionStatement}.", StringComparison.Ordinal))
            throw new ArgumentException("Enter the controlling office, determination date and complete approved distribution statement.");
    }

    internal static void ValidatePair(string? strCategory, string? strDistribution)
    {
        if (!((strCategory == strMarking && strDistribution == "") ||
            (strCategory == strCtiMarking && strDistribution is "B" or "C" or "D" or "E" or "F")))
            throw new ArgumentException("Select a supported CUI category and its distribution statement.");
    }

    internal static void ValidateRecord(CuiProtectedRecord objRecord)
    { ValidateMarking(objRecord.strClassification); Validate(objRecord.objCuiDesignation); }

    // Only fixed application aliases are passed here; values remain parameters.
    internal static string SqlScope(string strAlias) =>
        $"EXISTS(SELECT 1 FROM cui_policy_access p JOIN users u ON u.id=p.user_id WHERE p.user_id=$user AND p.cui_category={strAlias}.cui_category AND p.distribution_statement={strAlias}.distribution_statement AND p.status='Active' AND {strSqlRecipientScope})";

    internal const string strSqlRecipientScope = "(p.cui_category='CUI' OR (p.distribution_statement='B' AND u.cui_recipient_kind IN ('UsGovernment','DoD')) OR (p.distribution_statement='C' AND u.cui_recipient_kind IN ('UsGovernment','UsGovernmentContractor','DoD','UsDodContractor')) OR (p.distribution_statement='D' AND u.cui_recipient_kind IN ('DoD','UsDodContractor')) OR (p.distribution_statement='E' AND u.cui_recipient_kind='DoD') OR (p.distribution_statement='F' AND u.cui_recipient_kind IN ('UsGovernment','UsGovernmentContractor','DoD','UsDodContractor')))";

    internal static string AllowedDistributions(CuiRecipientKind enRecipient) => enRecipient switch
    {
        CuiRecipientKind.DoD => "BCDEF",
        CuiRecipientKind.UsGovernment => "BCF",
        CuiRecipientKind.UsDodContractor => "CDF",
        CuiRecipientKind.UsGovernmentContractor => "CF",
        _ => ""
    };

    internal static void Bind(SqliteCommand cmdPolicy, CuiDesignation objDesignation)
    {
        cmdPolicy.Parameters.AddWithValue("$category", objDesignation.strCategory);
        cmdPolicy.Parameters.AddWithValue("$distribution", objDesignation.strDistributionStatement);
        cmdPolicy.Parameters.AddWithValue("$office", objDesignation.strControllingOffice);
        cmdPolicy.Parameters.AddWithValue("$date", objDesignation.strDeterminationDate);
        cmdPolicy.Parameters.AddWithValue("$notice", objDesignation.strDistributionNotice);
    }

    internal static CuiDesignation Read(SqliteDataReader rdrPolicy, int iOffset = 0) => new(rdrPolicy.GetString(iOffset),
        rdrPolicy.GetString(iOffset + 1), rdrPolicy.GetString(iOffset + 2), rdrPolicy.GetString(iOffset + 3), rdrPolicy.GetString(iOffset + 4));

    internal static T Apply<T>(T objRecord, CuiDesignation objDesignation) where T : CuiProtectedRecord
    {
        objRecord.strCuiCategory = objDesignation.strCategory; objRecord.strDistributionStatement = objDesignation.strDistributionStatement;
        objRecord.strControllingOffice = objDesignation.strControllingOffice; objRecord.strDeterminationDate = objDesignation.strDeterminationDate;
        objRecord.strDistributionNotice = objDesignation.strDistributionNotice; return objRecord;
    }

    internal static void GrantDesignatorAccess(SqliteConnection dbConnection, SqliteTransaction txOperation,
        AuthorizedIdentity objIdentity, RecordKind enKind, long lngId, CuiDesignation objDesignation)
    {
        // Called only after authorized creation/redesignation. CTI never inherits
        // the Basic CUI administrator's global record override.
        if (objDesignation.strCategory == strCtiMarking)
            RetentionStatements.SetRecordAccess(dbConnection, txOperation, enKind, objIdentity.lngUserId, lngId, true, true);
    }

    internal static CuiDesignation ReadRecord(SqliteConnection dbConnection, SqliteTransaction txOperation, RecordKind enKind, long lngId)
    {
        var strTable = enKind == RecordKind.Contact ? "contacts" : "organizations";
        using var cmdPolicy = new SqliteCommand($"SELECT cui_category,distribution_statement,controlling_office,determination_date,distribution_notice FROM {strTable} WHERE id=$id", dbConnection, txOperation);
        cmdPolicy.Parameters.AddWithValue("$id", lngId);
        using var rdrPolicy = cmdPolicy.ExecuteReader();
        if (!rdrPolicy.Read()) throw new UnauthorizedAccessException("Select an authorized record.");
        var objPolicy = Read(rdrPolicy); Validate(objPolicy); return objPolicy;
    }

    internal static void ValidateMarking(string? strClassification)
    {
        if (!string.Equals(strClassification, strMarking, StringComparison.Ordinal))
            throw new ArgumentException("A valid CUI marking is required.");
    }
}
