using Microsoft.Data.Sqlite;

namespace HippoCrm.Core;

public sealed class Database
{
    private readonly string _connectionString;
    public string Path { get; }
    public Database(string path)
    {
        Path = System.IO.Path.GetFullPath(path);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = Path, ForeignKeys = true }.ToString();
        using var connection = Open();
        using var stream = typeof(Database).Assembly.GetManifestResourceStream("HippoCrm.Core.Schema.sql")!;
        using var reader = new StreamReader(stream);
        using var command = connection.CreateCommand();
        command.CommandText = reader.ReadToEnd();
        command.ExecuteNonQuery();
    }
    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }
    internal static SqliteCommand Command(SqliteConnection connection, string sql, params (string Name, object? Value)[] parameters)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command;
    }
    internal static string Now => DateTimeOffset.UtcNow.ToString("O");
}
