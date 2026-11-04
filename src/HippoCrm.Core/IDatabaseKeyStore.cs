namespace HippoCrm.Core;

public interface IDatabaseKeyStore
{
    void EnsureKey(string strDatabasePath, bool bAllowCreate);
    byte[] ReadKey(string strDatabasePath);
}
