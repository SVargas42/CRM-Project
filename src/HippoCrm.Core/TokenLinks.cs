using Microsoft.Data.Sqlite;

namespace HippoCrm.Core;

internal static class TokenLinks
{
    // Caller must already hold parent write authorization and the parent/audit transaction.
    // Global word identity is private implementation state, never a visibility grant.
    internal static void Synchronize(SqliteConnection dbConnection, SqliteTransaction txOperation,
        RecordKind enKind, long lngRecordId, string? strSkills, string? strTags)
    {
        try
        {
            if (enKind == RecordKind.Contact)
                SyncColumn(dbConnection, txOperation, lngRecordId, "skills", "contact_skills", "contact_id", "skill_id", strSkills);
            SyncColumn(dbConnection, txOperation, lngRecordId, "tags",
                enKind == RecordKind.Contact ? "contact_tags" : "organization_tags",
                enKind == RecordKind.Contact ? "contact_id" : "organization_id", "tag_id", strTags);
        }
        catch (Exception)
        {
            // Constraint/trigger errors may include word text. Never retain their inner exception.
            var strTable = enKind == RecordKind.Contact ? "contacts" : "organizations";
            throw new InvalidOperationException($"Link synchronization failed: {strTable}, record ID {lngRecordId}.");
        }
    }

    private static void SyncColumn(SqliteConnection dbConnection, SqliteTransaction txOperation, long lngRecordId,
        string strWords, string strLinks, string strParent, string strWordId, string? strText)
    {
        var dicWords = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var dicLinks = new Dictionary<long, string>();
        var setDesired = new HashSet<long>();
        try
        {
            // One complete word-table load per column/save, including words on inaccessible records.
            // Deterministic ID order preserves the first row if a historical Unicode duplicate exists.
            using (var cmdWords = Database.Command(dbConnection, $"SELECT id,name FROM {strWords} ORDER BY id"))
            {
                cmdWords.Transaction = txOperation;
                using var rdrWords = cmdWords.ExecuteReader();
                while (rdrWords.Read()) dicWords.TryAdd(rdrWords.GetString(1), rdrWords.GetInt64(0));
            }
            using (var cmdLinks = Database.Command(dbConnection, $"SELECT {strWordId},status FROM {strLinks} WHERE {strParent}=$id", ("$id", lngRecordId)))
            {
                cmdLinks.Transaction = txOperation;
                using var rdrLinks = cmdLinks.ExecuteReader();
                while (rdrLinks.Read()) dicLinks.Add(rdrLinks.GetInt64(0), rdrLinks.GetString(1));
            }
            foreach (var strWord in TokenWords.Parse(strText))
            {
                if (!dicWords.TryGetValue(strWord, out var lngWordId))
                {
                    try
                    {
                        using var cmdInsert = Database.Command(dbConnection, $"INSERT INTO {strWords}(name) VALUES($name) RETURNING id", ("$name", strWord));
                        cmdInsert.Transaction = txOperation; lngWordId = Convert.ToInt64(cmdInsert.ExecuteScalar());
                    }
                    catch (SqliteException objError) when (objError.SqliteExtendedErrorCode == 2067)
                    {
                        // SQLite's ASCII NOCASE unique index can arbitrate an insertion conflict.
                        using var cmdMatch = Database.Command(dbConnection, $"SELECT id FROM {strWords} WHERE name=$name COLLATE NOCASE ORDER BY id LIMIT 1", ("$name", strWord));
                        cmdMatch.Transaction = txOperation;
                        var objId = cmdMatch.ExecuteScalar();
                        if (objId is null or DBNull) throw;
                        lngWordId = Convert.ToInt64(objId);
                    }
                    dicWords.Add(strWord, lngWordId);
                }
                setDesired.Add(lngWordId);
            }
            // Retain all history, writing only actual differences; never churn an already Active link.
            foreach (var lngWordId in setDesired)
            {
                if (dicLinks.TryGetValue(lngWordId, out var strStatus) && strStatus == "Active") continue;
                using var cmdActivate = Database.Command(dbConnection,
                    $"INSERT INTO {strLinks}({strParent},{strWordId},status) VALUES($id,$word,'Active') ON CONFLICT({strParent},{strWordId}) DO UPDATE SET status='Active' WHERE status<>'Active'",
                    ("$id", lngRecordId), ("$word", lngWordId));
                cmdActivate.Transaction = txOperation; cmdActivate.ExecuteNonQuery();
            }
            foreach (var (lngWordId, strStatus) in dicLinks)
            {
                if (strStatus != "Active" || setDesired.Contains(lngWordId)) continue;
                using var cmdDeactivate = Database.Command(dbConnection,
                    $"UPDATE {strLinks} SET status='Inactive' WHERE {strParent}=$id AND {strWordId}=$word AND status='Active'", ("$id", lngRecordId), ("$word", lngWordId));
                cmdDeactivate.Transaction = txOperation; cmdDeactivate.ExecuteNonQuery();
            }
        }
        finally { dicWords.Clear(); dicLinks.Clear(); setDesired.Clear(); }
    }
}
