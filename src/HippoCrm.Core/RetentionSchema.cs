using Microsoft.Data.Sqlite;
namespace HippoCrm.Core;

internal static class RetentionSchema
{
    private static readonly string[] arrTables = ["sessions", "contact_access", "organization_access", "contact_organizations"];
    internal static void Initialize(SqliteConnection dbConnection, SqliteTransaction txOperation)
    {
        foreach (var strTable in arrTables)
        {
            using var cmdCheck = new SqliteCommand("SELECT count(*) FROM pragma_table_info($table) WHERE name='status'", dbConnection, txOperation);
            cmdCheck.Parameters.AddWithValue("$table", strTable);
            if (Convert.ToInt64(cmdCheck.ExecuteScalar()) == 0)
            {
                using var cmdAlter = new SqliteCommand($"ALTER TABLE {strTable} ADD COLUMN status TEXT NOT NULL DEFAULT 'Active' CHECK(status IN ('Active','Inactive','Retired','Deceased'))", dbConnection, txOperation);
                cmdAlter.ExecuteNonQuery();
            }
            using var cmdProtect = new SqliteCommand($"CREATE TRIGGER IF NOT EXISTS protect_{strTable}_retention BEFORE DELETE ON {strTable} BEGIN SELECT RAISE(ABORT,'Deletion is prohibited'); END;", dbConnection, txOperation);
            cmdProtect.ExecuteNonQuery();
        }
        foreach (var strTable in new[] { "contacts", "organizations" })
        {
            foreach (var strEvent in new[] { "INSERT", "UPDATE" })
            {
                using var cmdLifecycle = new SqliteCommand($"CREATE TRIGGER IF NOT EXISTS retain_{strTable}_{strEvent} BEFORE {strEvent} ON {strTable} WHEN NEW.archived=1 AND NEW.status='Active' BEGIN SELECT RAISE(ABORT,'Archived records cannot be active'); END;", dbConnection, txOperation);
                cmdLifecycle.ExecuteNonQuery();
            }
        }
    }
}
