using Microsoft.Data.Sqlite;

namespace HippoCrm.Core;

internal static class SchemaManager
{
    internal static void Initialize(SqliteConnection dbConnection, SqliteTransaction txSchema)
    {
        using var cmdExists = Database.Command(dbConnection, "SELECT count(*) FROM sqlite_master WHERE type='table' AND name='schema_version'");
        cmdExists.Transaction = txSchema;
        var bMigrated = false; var bRequireExistingAnchor = false;
        var bRelationshipMigrated = false;
        var bContactMetadataMigrated = false;
        var bClassificationMigration = false;
        var bCuiPolicyMigration = true;
        if ((long)cmdExists.ExecuteScalar()! != 0)
        {
            using var cmdVersion = Database.Command(dbConnection, "SELECT version FROM schema_version");
            cmdVersion.Transaction = txSchema;
            using var rdrVersion = cmdVersion.ExecuteReader();
            if (!rdrVersion.Read()) throw new InvalidOperationException("The workspace version cannot be verified.");
            var lngVersion = rdrVersion.GetInt64(0);
            if (rdrVersion.Read() || lngVersion is not (1 or 2 or 3 or 4 or 5 or 6 or 7 or 8 or 9))
                throw new InvalidOperationException("This workspace version requires administrator review.");
            bRequireExistingAnchor = lngVersion >= 6;
            bClassificationMigration = lngVersion < 7;
            bCuiPolicyMigration = lngVersion < 9;
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
        using var cmdUpgrade = Database.Command(dbConnection, "UPDATE schema_version SET version=9 WHERE version<>9");
        cmdUpgrade.Transaction = txSchema;
        var bUnifiedMigrated = cmdUpgrade.ExecuteNonQuery() != 0;
        if (bMigrated) SecurityAudit.Append(dbConnection, txSchema, null, AuditEntity.Schema, 2, AuditAction.SchemaUpgraded);
        if (bRelationshipMigrated) SecurityAudit.Append(dbConnection, txSchema, null, AuditEntity.Schema, 3, AuditAction.SchemaUpgraded);
        if (bContactMetadataMigrated) SecurityAudit.Append(dbConnection, txSchema, null, AuditEntity.Schema, 4, AuditAction.SchemaUpgraded);
        if (bUnifiedMigrated || bNamePartsMigrated || bCuiPolicyMigration) SecurityAudit.Append(dbConnection, txSchema, null, AuditEntity.Schema, 9, AuditAction.SchemaUpgraded);
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
