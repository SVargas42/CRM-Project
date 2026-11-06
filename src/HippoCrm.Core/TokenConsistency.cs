using Microsoft.Data.Sqlite;

namespace HippoCrm.Core;

internal sealed record TokenMismatch(string strTable, long lngRecordId, string strColumn, string strKind);

public sealed partial class CrmService
{
    // Metadata only. One protected read/audit verification covers all bounded batches.
    // All retained lifecycle states are checked under the unchanged record/category scopes.
    internal IReadOnlyList<TokenMismatch> CheckTokenConsistency() => ReadOperation((dbConnection, txOperation, objIdentity) =>
    {
        var lstMismatches = new List<TokenMismatch>();
        var bTransferred = false;
        try
        {
            foreach (var bContact in new[] { true, false })
            {
                var strTable = bContact ? "contacts" : "organizations";
                var strAlias = bContact ? "c" : "o";
                var strScope = bContact ? strContactScope : strOrganizationScope;
                long? lngAfter = null;
                while (true)
                {
                    var lstBatch = new List<(long lngId, string strSkills, string strTags)>();
                    try
                    {
                        using (var cmdBatch = ScopedCommand(dbConnection, txOperation, objIdentity,
                            $"SELECT {strAlias}.id,{(bContact ? "c.skills" : "''")},{strAlias}.tags FROM {strTable} {strAlias} WHERE {strScope} AND ($after IS NULL OR {strAlias}.id>$after) ORDER BY {strAlias}.id LIMIT 500", ("$after", lngAfter)))
                        {
                            using var rdrBatch = cmdBatch.ExecuteReader();
                            while (rdrBatch.Read()) lstBatch.Add((rdrBatch.GetInt64(0), rdrBatch.IsDBNull(1) ? "" : rdrBatch.GetString(1), rdrBatch.IsDBNull(2) ? "" : rdrBatch.GetString(2)));
                        }
                        if (lstBatch.Count == 0) break;
                        foreach (var (lngId, strSkills, strTags) in lstBatch)
                        {
                            if (bContact) CompareColumn(dbConnection, txOperation, lstMismatches, strTable, lngId,
                                "skills", "skills", "contact_skills", "contact_id", "skill_id", strSkills);
                            CompareColumn(dbConnection, txOperation, lstMismatches, strTable, lngId,
                                "tags", "tags", bContact ? "contact_tags" : "organization_tags", bContact ? "contact_id" : "organization_id", "tag_id", strTags);
                        }
                        lngAfter = lstBatch[^1].lngId;
                    }
                    finally { CuiMemoryPolicy.Release(lstBatch); }
                }
            }
            bTransferred = true; return (IReadOnlyList<TokenMismatch>)lstMismatches;
        }
        finally { if (!bTransferred) CuiMemoryPolicy.Release(lstMismatches); }
    });

    private static void CompareColumn(SqliteConnection dbConnection, SqliteTransaction txOperation,
        List<TokenMismatch> lstMismatches, string strTable, long lngId, string strColumn,
        string strWords, string strLinks, string strParent, string strWordId, string strText)
    {
        var setExpected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var setActual = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var strWord in TokenWords.Parse(strText)) setExpected.Add(strWord);
            // Parent was authorized in this same immediate read transaction before its links are read.
            using (var cmdLinks = Database.Command(dbConnection,
                $"SELECT w.name FROM {strLinks} l JOIN {strWords} w ON w.id=l.{strWordId} WHERE l.{strParent}=$id AND l.status='Active'", ("$id", lngId)))
            {
                cmdLinks.Transaction = txOperation;
                using var rdrLinks = cmdLinks.ExecuteReader();
                while (rdrLinks.Read()) setActual.Add(rdrLinks.GetString(0));
            }
            if (setExpected.Except(setActual, StringComparer.OrdinalIgnoreCase).Any())
                lstMismatches.Add(new TokenMismatch(strTable, lngId, strColumn, "MissingLink"));
            if (setActual.Except(setExpected, StringComparer.OrdinalIgnoreCase).Any())
                lstMismatches.Add(new TokenMismatch(strTable, lngId, strColumn, "ExtraActiveLink"));
        }
        catch (Exception) { throw new InvalidOperationException($"Consistency check failed: {strTable}, record ID {lngId}."); }
        finally { setExpected.Clear(); setActual.Clear(); }
    }
}
