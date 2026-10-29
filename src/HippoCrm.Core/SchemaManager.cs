using Microsoft.Data.Sqlite;

namespace HippoCrm.Core;

internal static class SchemaManager
{
    internal static void Initialize(SqliteConnection dbConnection, SqliteTransaction txSchema)
    {
        using var cmdExists = Database.Command(dbConnection, "SELECT count(*) FROM sqlite_master WHERE type='table' AND name='schema_version'");
        cmdExists.Transaction = txSchema;
        var bMigrated = false;
        if ((long)cmdExists.ExecuteScalar()! != 0)
        {
            using var cmdVersion = Database.Command(dbConnection, "SELECT version FROM schema_version");
            cmdVersion.Transaction = txSchema;
            using var rdrVersion = cmdVersion.ExecuteReader();
            if (!rdrVersion.Read()) throw new InvalidOperationException("The workspace version cannot be verified.");
            var lngVersion = rdrVersion.GetInt64(0);
            if (rdrVersion.Read() || lngVersion is not (1 or 2))
                throw new InvalidOperationException("This workspace version requires administrator review.");
            rdrVersion.Close();
            if (lngVersion == 1)
            {
                using var cmdUsers = Database.Command(dbConnection, "SELECT count(*) FROM users");
                cmdUsers.Transaction = txSchema;
                if ((long)cmdUsers.ExecuteScalar()! > 1)
                    throw new InvalidOperationException("Legacy user permissions require explicit administrator mapping before upgrade.");
                ExecuteResource(dbConnection, txSchema, "Migration002.sql");
                bMigrated = true;
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
        if (bMigrated) SecurityAudit.Append(dbConnection, txSchema, null, AuditEntity.Schema, 2, AuditAction.SchemaUpgraded);
    }

    private static void ExecuteResource(SqliteConnection dbConnection, SqliteTransaction txSchema, string strResource)
    {
        using var stmSchema = typeof(SchemaManager).Assembly.GetManifestResourceStream("HippoCrm.Core." + strResource)
            ?? throw new InvalidOperationException("The workspace cannot be initialized.");
        using var rdrSchema = new StreamReader(stmSchema);
        using var cmdSchema = Database.Command(dbConnection, rdrSchema.ReadToEnd());
        cmdSchema.Transaction = txSchema;
        cmdSchema.ExecuteNonQuery();
    }

}
