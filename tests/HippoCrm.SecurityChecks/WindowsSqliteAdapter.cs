using System.Globalization;
using System.Runtime.InteropServices;

namespace Microsoft.Data.Sqlite;

// TEST-ONLY adapter for the Windows-bundled SQLite engine. No package/feed/network is used.
public sealed class SqliteException(string strMessage) : Exception(strMessage);
public sealed class SqliteConnection(string strUri) : IDisposable
{
    internal IntPtr ptrDatabase;
    public void Open()
    {
        if (NativeSqlite.Open(strUri, out ptrDatabase, 2 | 4 | 0x40 | 0x10000, IntPtr.Zero) != 0) throw new SqliteException("Test SQLite open failed.");
        NativeSqlite.BusyTimeout(ptrDatabase, 1000);
    }
    public SqliteCommand CreateCommand() => new(this);
    public SqliteTransaction BeginTransaction(bool bDeferred = false) => new(this, bDeferred);
    public void Dispose() { if (ptrDatabase != IntPtr.Zero) { NativeSqlite.Close(ptrDatabase); ptrDatabase = IntPtr.Zero; } }
}
public sealed class SqliteTransaction : IDisposable
{
    private readonly SqliteConnection dbConnection;
    private bool bCompleted;
    internal SqliteTransaction(SqliteConnection dbConnection, bool bDeferred)
    {
        this.dbConnection = dbConnection; Execute(bDeferred ? "BEGIN DEFERRED" : "BEGIN IMMEDIATE");
    }
    private void Execute(string strSql) { using var cmdOperation = dbConnection.CreateCommand(); cmdOperation.CommandText = strSql; cmdOperation.ExecuteNonQuery(); }
    public void Commit() { Execute("COMMIT"); bCompleted = true; }
    public void Dispose() { if (!bCompleted) { Execute("ROLLBACK"); bCompleted = true; } }
}
public sealed class SqliteParameterCollection
{
    internal readonly Dictionary<string, object> dicValues = new();
    public void AddWithValue(string strName, object objValue) => dicValues.Add(strName, objValue);
}
public sealed class SqliteCommand(SqliteConnection dbConnection) : IDisposable
{
    public string CommandText { get; set; } = "";
    public SqliteTransaction? Transaction { get; set; }
    public SqliteParameterCollection Parameters { get; } = new();
    private void Bind(IntPtr ptrStatement)
    {
        foreach (var (strName, objValue) in Parameters.dicValues)
        {
            var iIndex = NativeSqlite.ParameterIndex(ptrStatement, strName);
            if (iIndex == 0) continue; // Production SQL sometimes supplies unused parameters.
            var iResult = objValue switch
            {
                DBNull => NativeSqlite.BindNull(ptrStatement, iIndex),
                byte[] arrBytes => NativeSqlite.BindBlob(ptrStatement, iIndex, arrBytes, arrBytes.Length, new IntPtr(-1)),
                string strValue => NativeSqlite.BindText(ptrStatement, iIndex, strValue, -1, new IntPtr(-1)),
                bool bValue => NativeSqlite.BindInt64(ptrStatement, iIndex, bValue ? 1 : 0),
                _ => NativeSqlite.BindInt64(ptrStatement, iIndex, Convert.ToInt64(objValue, CultureInfo.InvariantCulture))
            };
            Check(iResult);
        }
    }
    private void Check(int iResult)
    {
        if (iResult != 0) throw new SqliteException("Test SQLite operation rejected: " + iResult.ToString(CultureInfo.InvariantCulture));
    }
    public int ExecuteNonQuery()
    {
        var ptrSql = Marshal.StringToCoTaskMemUTF8(CommandText);
        var ptrRemaining = ptrSql;
        var iChanges = 0;
        try
        {
            while (Marshal.ReadByte(ptrRemaining) != 0)
            {
                Check(NativeSqlite.Prepare(dbConnection.ptrDatabase, ptrRemaining, -1, out var ptrStatement, out var ptrTail));
                ptrRemaining = ptrTail;
                if (ptrStatement == IntPtr.Zero) continue;
                try
                {
                    Bind(ptrStatement);
                    var iResult = NativeSqlite.Step(ptrStatement);
                    while (iResult == 100) iResult = NativeSqlite.Step(ptrStatement);
                    if (iResult != 101) Check(iResult);
                    iChanges += NativeSqlite.Changes(dbConnection.ptrDatabase);
                }
                finally { NativeSqlite.Finalize(ptrStatement); }
            }
            return iChanges;
        }
        finally { Marshal.FreeCoTaskMem(ptrSql); }
    }
    public SqliteDataReader ExecuteReader()
    {
        var ptrSql = Marshal.StringToCoTaskMemUTF8(CommandText);
        IntPtr ptrStatement = IntPtr.Zero;
        try
        {
            Check(NativeSqlite.Prepare(dbConnection.ptrDatabase, ptrSql, -1, out ptrStatement, out var ptrTail));
            Bind(ptrStatement); return new SqliteDataReader(ptrStatement);
        }
        catch { if (ptrStatement != IntPtr.Zero) NativeSqlite.Finalize(ptrStatement); throw; }
        finally { Marshal.FreeCoTaskMem(ptrSql); }
    }
    public object? ExecuteScalar() { using var rdrResult = ExecuteReader(); return rdrResult.Read() ? rdrResult[0] : null; }
    public void Dispose() { }
}
public sealed class SqliteDataReader(IntPtr ptrStatement) : IDisposable
{
    private IntPtr ptrCurrent = ptrStatement;
    public bool Read()
    {
        var iResult = NativeSqlite.Step(ptrCurrent);
        if (iResult is not (100 or 101)) throw new SqliteException("Test SQLite query rejected: " + iResult);
        return iResult == 100;
    }
    public long GetInt64(int iColumn) => NativeSqlite.ColumnInt64(ptrCurrent, iColumn);
    public int GetInt32(int iColumn) => checked((int)GetInt64(iColumn));
    public string GetString(int iColumn) => Marshal.PtrToStringUTF8(NativeSqlite.ColumnText(ptrCurrent, iColumn))!;
    public bool IsDBNull(int iColumn) => NativeSqlite.ColumnType(ptrCurrent, iColumn) == 5;
    public object this[string strColumn]
    {
        get
        {
            for (var iColumn = 0; iColumn < NativeSqlite.ColumnCount(ptrCurrent); iColumn++)
                if (Marshal.PtrToStringUTF8(NativeSqlite.ColumnName(ptrCurrent, iColumn)) == strColumn) return this[iColumn];
            throw new IndexOutOfRangeException(strColumn);
        }
    }
    public object this[int iColumn]
    {
        get
        {
            return NativeSqlite.ColumnType(ptrCurrent, iColumn) switch
            {
                1 => GetInt64(iColumn), 3 => GetString(iColumn), 4 => Blob(iColumn), 5 => DBNull.Value,
                _ => throw new InvalidOperationException("Unsupported test SQLite value.")
            };
        }
    }
    private byte[] Blob(int iColumn)
    {
        var arrBytes = new byte[NativeSqlite.ColumnBytes(ptrCurrent, iColumn)];
        Marshal.Copy(NativeSqlite.ColumnBlob(ptrCurrent, iColumn), arrBytes, 0, arrBytes.Length); return arrBytes;
    }
    public void Close() { if (ptrCurrent != IntPtr.Zero) { NativeSqlite.Finalize(ptrCurrent); ptrCurrent = IntPtr.Zero; } }
    public void Dispose() => Close();
}
internal static class NativeSqlite
{
    [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_open_v2", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int Open([MarshalAs(UnmanagedType.LPUTF8Str)] string strPath, out IntPtr ptrDatabase, int iFlags, IntPtr ptrVfs);
    [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_close_v2", CallingConvention = CallingConvention.Cdecl)] internal static extern int Close(IntPtr ptrDatabase);
    [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_busy_timeout", CallingConvention = CallingConvention.Cdecl)] internal static extern int BusyTimeout(IntPtr ptrDatabase, int iMilliseconds);
    [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_prepare_v2", CallingConvention = CallingConvention.Cdecl)] internal static extern int Prepare(IntPtr ptrDatabase, IntPtr ptrSql, int iBytes, out IntPtr ptrStatement, out IntPtr ptrTail);
    [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_step", CallingConvention = CallingConvention.Cdecl)] internal static extern int Step(IntPtr ptrStatement);
    [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_finalize", CallingConvention = CallingConvention.Cdecl)] internal static extern int Finalize(IntPtr ptrStatement);
    [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_changes", CallingConvention = CallingConvention.Cdecl)] internal static extern int Changes(IntPtr ptrDatabase);
    [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_bind_parameter_index", CallingConvention = CallingConvention.Cdecl)] internal static extern int ParameterIndex(IntPtr ptrStatement, [MarshalAs(UnmanagedType.LPUTF8Str)] string strName);
    [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_bind_null", CallingConvention = CallingConvention.Cdecl)] internal static extern int BindNull(IntPtr ptrStatement, int iIndex);
    [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_bind_int64", CallingConvention = CallingConvention.Cdecl)] internal static extern int BindInt64(IntPtr ptrStatement, int iIndex, long lngValue);
    [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_bind_text", CallingConvention = CallingConvention.Cdecl)] internal static extern int BindText(IntPtr ptrStatement, int iIndex, [MarshalAs(UnmanagedType.LPUTF8Str)] string strValue, int iBytes, IntPtr ptrDestructor);
    [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_bind_blob", CallingConvention = CallingConvention.Cdecl)] internal static extern int BindBlob(IntPtr ptrStatement, int iIndex, byte[] arrBytes, int iBytes, IntPtr ptrDestructor);
    [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_column_int64", CallingConvention = CallingConvention.Cdecl)] internal static extern long ColumnInt64(IntPtr ptrStatement, int iColumn);
    [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_column_text", CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr ColumnText(IntPtr ptrStatement, int iColumn);
    [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_column_blob", CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr ColumnBlob(IntPtr ptrStatement, int iColumn);
    [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_column_bytes", CallingConvention = CallingConvention.Cdecl)] internal static extern int ColumnBytes(IntPtr ptrStatement, int iColumn);
    [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_column_type", CallingConvention = CallingConvention.Cdecl)] internal static extern int ColumnType(IntPtr ptrStatement, int iColumn);
    [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_column_count", CallingConvention = CallingConvention.Cdecl)] internal static extern int ColumnCount(IntPtr ptrStatement);
    [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_column_name", CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr ColumnName(IntPtr ptrStatement, int iColumn);
}
