using Microsoft.Data.Sqlite;

namespace HippoCrm.Core;

internal static class SchemaManager
{
    internal static IReadOnlyList<SkippedToken> Initialize(SqliteConnection dbConnection, SqliteTransaction txSchema)
    {
        using var cmdExists = Database.Command(dbConnection, "SELECT count(*) FROM sqlite_master WHERE type='table' AND name='schema_version'");
        cmdExists.Transaction = txSchema;
        var bMigrated = false; var bRequireExistingAnchor = false;
        var bRelationshipMigrated = false;
        var bContactMetadataMigrated = false;
        var bClassificationMigration = false;
        var bCuiPolicyMigration = true;
        var bTokenMigration = true;
        IReadOnlyList<SkippedToken> lstSkipped = Array.Empty<SkippedToken>();
        if ((long)cmdExists.ExecuteScalar()! != 0)
        {
            using var cmdVersion = Database.Command(dbConnection, "SELECT version FROM schema_version");
            cmdVersion.Transaction = txSchema;
            using var rdrVersion = cmdVersion.ExecuteReader();
            if (!rdrVersion.Read()) throw new InvalidOperationException("The workspace version cannot be verified.");
            var lngVersion = rdrVersion.GetInt64(0);
            if (rdrVersion.Read() || lngVersion is not (1 or 2 or 3 or 4 or 5 or 6 or 7 or 8 or 9 or 10))
                throw new InvalidOperationException("This workspace version requires administrator review.");
            bRequireExistingAnchor = lngVersion >= 6;
            bClassificationMigration = lngVersion < 7;
            bCuiPolicyMigration = lngVersion < 9;
            bTokenMigration = lngVersion < 10;
            rdrVersion.Close();
            if (lngVersion == 1)
            {
                using var cmdUsers = Database.Command(dbConnection, "SELECT count(*) FROM users");
                cmdUsers.Transaction = txSchema;
                if ((long)cmdUsers.ExecuteScalar()! > 1)
                    throw new InvalidOperationException("Legacy user permissions require explicit administrator mapping before upgrade.");
                ExecuteResource(dbConnection, txSchema, "Migration002.sql");
                bMigrated = true; lngVersion = 2;
            }
            if (lngVersion is 2 or 3)
            {
                // Both branches used version 3: main added revisions; test1 added organization_id.
                // Inspect the actual columns before applying the additive relationship migrations.
                if (!HasColumn(dbConnection, txSchema, "contacts", "organization_id"))
                {
                    ExecuteResource(dbConnection, txSchema, "Migration003.sql");
                    bRelationshipMigrated = true;
                }
                using var cmdRelationshipVersion = Database.Command(dbConnection, "UPDATE schema_version SET version=3 WHERE version=2");
                cmdRelationshipVersion.Transaction = txSchema; cmdRelationshipVersion.ExecuteNonQuery();
                if (!HasColumn(dbConnection, txSchema, "contacts", "tags"))
                {
                    ExecuteResource(dbConnection, txSchema, "Migration004.sql");
                    bContactMetadataMigrated = true;
                }
            }
        }
        else
        {
            using var cmdUnversioned = Database.Command(dbConnection, "SELECT count(*) FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'");
            cmdUnversioned.Transaction = txSchema;
            if ((long)cmdUnversioned.ExecuteScalar()! != 0)
                throw new InvalidOperationException("An unversioned workspace requires administrator review.");
        }
        ExecuteResource(dbConnection, txSchema, "Schema.sql");
        foreach (var strTable in new[] { "contacts", "organizations" })
        {
            if (HasColumn(dbConnection, txSchema, strTable, "revision")) continue;
            using var cmdRevision = Database.Command(dbConnection, $"ALTER TABLE {strTable} ADD COLUMN revision INTEGER NOT NULL DEFAULT 0");
            cmdRevision.Transaction = txSchema; cmdRevision.ExecuteNonQuery();
        }
        RetentionSchema.Initialize(dbConnection, txSchema);
        SecurityAudit.Initialize(dbConnection, txSchema, bRequireExistingAnchor);
        if (bClassificationMigration) ExecuteResource(dbConnection, txSchema, "Migration007.sql");
        // macOS used version 6 for name fields; HEAD used 6/7 for audit/CUI.
        // Inspect columns instead of replaying Migration006 or downgrading either lineage.
        var bNamePartsMigrated = false;
        foreach (var strColumn in new[] { "middle_names", "prefix", "suffix" })
        {
            if (HasColumn(dbConnection, txSchema, "contacts", strColumn)) continue;
            using var cmdNamePart = Database.Command(dbConnection, $"ALTER TABLE contacts ADD COLUMN {strColumn} TEXT NOT NULL DEFAULT ''");
            cmdNamePart.Transaction = txSchema; cmdNamePart.ExecuteNonQuery(); bNamePartsMigrated = true;
        }
        if (bCuiPolicyMigration)
        {
            if (!HasColumn(dbConnection, txSchema, "users", "cui_recipient_kind"))
            {
                using var cmdRecipients = Database.Command(dbConnection, "ALTER TABLE users ADD COLUMN cui_recipient_kind TEXT NOT NULL DEFAULT 'Unverified' CHECK(cui_recipient_kind IN ('Unverified','UsGovernment','UsGovernmentContractor','DoD','UsDodContractor')); ALTER TABLE users ADD COLUMN cui_recipient_evidence TEXT NOT NULL DEFAULT '' CHECK(length(cui_recipient_evidence)<=256);");
                cmdRecipients.Transaction = txSchema; cmdRecipients.ExecuteNonQuery();
            }
            // Add columns without rebuilding/deleting retained tables or historical records.
            foreach (var strTable in new[] { "contacts", "organizations", "profile_images" })
                foreach (var strColumn in new[] { "cui_category", "distribution_statement", "controlling_office", "determination_date", "distribution_notice" })
                {
                    if (HasColumn(dbConnection, txSchema, strTable, strColumn)) continue;
                    var strDefault = strColumn == "cui_category" ? "CUI" : "";
                    using var cmdPolicyColumn = Database.Command(dbConnection, $"ALTER TABLE {strTable} ADD COLUMN {strColumn} TEXT NOT NULL DEFAULT '{strDefault}'");
                    cmdPolicyColumn.Transaction = txSchema; cmdPolicyColumn.ExecuteNonQuery();
                }
            ExecuteResource(dbConnection, txSchema, "Migration009.sql");
        }
        using var cmdUpgrade = Database.Command(dbConnection, "UPDATE schema_version SET version=9 WHERE version<9");
        cmdUpgrade.Transaction = txSchema;
        var bUnifiedMigrated = cmdUpgrade.ExecuteNonQuery() != 0;
        if (bMigrated) SecurityAudit.Append(dbConnection, txSchema, null, AuditEntity.Schema, 2, AuditAction.SchemaUpgraded);
        if (bRelationshipMigrated) SecurityAudit.Append(dbConnection, txSchema, null, AuditEntity.Schema, 3, AuditAction.SchemaUpgraded);
        if (bContactMetadataMigrated) SecurityAudit.Append(dbConnection, txSchema, null, AuditEntity.Schema, 4, AuditAction.SchemaUpgraded);
        if (bUnifiedMigrated || bNamePartsMigrated || bCuiPolicyMigration) SecurityAudit.Append(dbConnection, txSchema, null, AuditEntity.Schema, 9, AuditAction.SchemaUpgraded);
        if (bTokenMigration)
        {
            ExecuteResource(dbConnection, txSchema, "Migration010.sql");
            lstSkipped = BackfillTokens(dbConnection, txSchema);
            using var cmdTokens = Database.Command(dbConnection, "UPDATE schema_version SET version=10 WHERE version<>10");
            cmdTokens.Transaction = txSchema; cmdTokens.ExecuteNonQuery();
            SecurityAudit.Append(dbConnection, txSchema, null, AuditEntity.Schema, 10, AuditAction.SchemaUpgraded);
        }
        return lstSkipped;
    }

    // Only value-free location metadata may escape migration for administrator review.
    internal sealed record SkippedToken(string strTable, long lngRecordId, string strColumn);

    private static IReadOnlyList<SkippedToken> BackfillTokens(SqliteConnection dbConnection, SqliteTransaction txSchema)
    {
        var lstSkipped = new List<SkippedToken>();
        // Schema-owned identifiers only. Services deliberately have no word table.
        foreach (var (strTable, strColumn, strWords, strLinks, strParent, strWord) in new[]
        {
            ("contacts", "skills", "skills", "contact_skills", "contact_id", "skill_id"),
            ("contacts", "tags", "tags", "contact_tags", "contact_id", "tag_id"),
            ("organizations", "tags", "tags", "organization_tags", "organization_id", "tag_id")
        })
        {
            long? lngAfter = null;
            while (true)
            {
                var lstBatch = new List<(long lngId, string strText)>();
                try
                {
                    using (var cmdBatch = Database.Command(dbConnection,
                        $"SELECT id,{strColumn} FROM {strTable} WHERE ($after IS NULL OR id>$after) ORDER BY id LIMIT 500", ("$after", lngAfter)))
                    {
                        cmdBatch.Transaction = txSchema;
                        using var rdrBatch = cmdBatch.ExecuteReader();
                        while (rdrBatch.Read()) lstBatch.Add((rdrBatch.GetInt64(0), rdrBatch.IsDBNull(1) ? "" : rdrBatch.GetString(1)));
                    }
                    if (lstBatch.Count == 0) break;
                    // Close each batch reader before inserting; never normalize authoritative text.
                    foreach (var (lngId, strText) in lstBatch)
                    {
                        try
                        {
                            foreach (var strName in TokenWords.Parse(strText,
                                () => lstSkipped.Add(new SkippedToken(strTable, lngId, strColumn))))
                            {
                                var lngWordId = FindWord(dbConnection, txSchema, strWords, strName);
                                if (lngWordId is null)
                                {
                                    using var cmdWord = Database.Command(dbConnection, $"INSERT INTO {strWords}(name) VALUES($name) RETURNING id", ("$name", strName));
                                    cmdWord.Transaction = txSchema; lngWordId = Convert.ToInt64(cmdWord.ExecuteScalar());
                                }
                                using var cmdLink = Database.Command(dbConnection,
                                    $"INSERT INTO {strLinks}({strParent},{strWord}) VALUES($parent,$word) ON CONFLICT({strParent},{strWord}) DO NOTHING",
                                    ("$parent", lngId), ("$word", lngWordId));
                                cmdLink.Transaction = txSchema; cmdLink.ExecuteNonQuery();
                            }
                        }
                        catch (Exception)
                        {
                            // Trigger/provider messages can contain data; do not retain an inner exception.
                            throw new InvalidOperationException($"Token migration failed: {strTable}, record ID {lngId}, column {strColumn}.");
                        }
                    }
                    lngAfter = lstBatch[^1].lngId;
                }
                finally { lstBatch.Clear(); }
            }
        }
        return lstSkipped.AsReadOnly();
    }

    private static long? FindWord(SqliteConnection dbConnection, SqliteTransaction txSchema, string strWords, string strName)
    {
        // Word tables hold words, not visibility grants: never read them directly for pickers/reports.
        // SQLite NOCASE covers ASCII only. Bounded scans retain OrdinalIgnoreCase semantics for
        // Unicode without keeping an unbounded global word dictionary in managed memory.
        long? lngAfter = null;
        while (true)
        {
            using var cmdWords = Database.Command(dbConnection,
                $"SELECT id,name FROM {strWords} WHERE ($after IS NULL OR id>$after) ORDER BY id LIMIT 500", ("$after", lngAfter));
            cmdWords.Transaction = txSchema;
            using var rdrWords = cmdWords.ExecuteReader();
            var iRead = 0;
            while (rdrWords.Read())
            {
                lngAfter = rdrWords.GetInt64(0); iRead++;
                if (string.Equals(rdrWords.GetString(1), strName, StringComparison.OrdinalIgnoreCase)) return lngAfter;
            }
            if (iRead < 500) return null;
        }
    }

    private static bool HasColumn(SqliteConnection dbConnection, SqliteTransaction txSchema, string strTable, string strColumn)
    {
        using var cmdColumn = Database.Command(dbConnection,
            "SELECT count(*) FROM pragma_table_info($table) WHERE name=$column", ("$table", strTable), ("$column", strColumn));
        cmdColumn.Transaction = txSchema;
        return (long)cmdColumn.ExecuteScalar()! != 0;
    }

    private static void ExecuteResource(SqliteConnection dbConnection, SqliteTransaction txSchema, string strResource)
    {
        using var stmSchema = typeof(SchemaManager).Assembly.GetManifestResourceStream("HippoCrm.Core." + strResource)
            ?? throw new InvalidOperationException("The workspace cannot be initialized.");
        using var rdrSchema = new StreamReader(stmSchema);
        using var cmdSchema = Database.Command(dbConnection, rdrSchema.ReadToEnd());
        cmdSchema.Transaction = txSchema; cmdSchema.ExecuteNonQuery();
    }
}
