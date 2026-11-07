using System.Security.Cryptography;

namespace HippoCrm.Desktop;

public static class LocalImageImport
{
    public const int iMaximumSourceBytes=10*1024*1024;
    public static async Task<byte[]> ReadAsync(string strPath,CancellationToken ctCancellation=default)
    {
        byte[]? arrInput=null;
        try
        {
            ctCancellation.ThrowIfCancellationRequested();
            var strLocal=ValidateLocalPath(strPath);
            await using var stmSource=new FileStream(strLocal,FileMode.Open,FileAccess.Read,FileShare.Read,81920,FileOptions.Asynchronous|FileOptions.SequentialScan);
            if(stmSource.Length is < 8 or > iMaximumSourceBytes) throw new ArgumentException("Choose a PNG or JPEG picture under 10 MB.");
            arrInput=GC.AllocateArray<byte>(checked((int)stmSource.Length),pinned:true);
            await stmSource.ReadExactlyAsync(arrInput,ctCancellation);
            ctCancellation.ThrowIfCancellationRequested();
            // Recheck path attributes after reading; never retain source paths in CRM data.
            ValidateLocalPath(strLocal);
            var arrResult=arrInput; arrInput=null; return arrResult;
        }
        catch(OperationCanceledException) { throw; }
        catch(Exception objException) when(objException is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { throw new ArgumentException("Choose a downloaded PNG or JPEG under 10 MB from a fully local folder outside cloud storage."); }
        finally { if(arrInput is not null) CryptographicOperations.ZeroMemory(arrInput); }
    }
    public static string ValidateLocalPath(string strPath)
    {
        if(!Path.IsPathFullyQualified(strPath) || strPath.StartsWith(@"\\",StringComparison.Ordinal) || strPath.Contains("://",StringComparison.Ordinal))
            throw new ArgumentException("Choose a fully local picture.");
        var strFull=Path.GetFullPath(strPath);
        var objMount=DriveInfo.GetDrives().Where(objDrive=>strFull.StartsWith(objDrive.Name.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)+Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(objDrive=>objDrive.Name.Length).FirstOrDefault();
        if(objMount is null || objMount.DriveType is not (DriveType.Fixed or DriveType.Removable or DriveType.Ram or DriveType.CDRom))
            throw new ArgumentException("Network pictures are not supported.");
        var strCurrent=Path.GetPathRoot(strFull)!;
        foreach(var strPart in strFull[strCurrent.Length..].Split([Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar],StringSplitOptions.RemoveEmptyEntries))
        {
            if(strPart.StartsWith("OneDrive",StringComparison.OrdinalIgnoreCase) || strPart.Equals("CloudStorage",StringComparison.OrdinalIgnoreCase)
                || strPart.Equals("Mobile Documents",StringComparison.OrdinalIgnoreCase) || strPart.Equals("Dropbox",StringComparison.OrdinalIgnoreCase)
                || strPart.StartsWith("GoogleDrive",StringComparison.OrdinalIgnoreCase) || strPart.StartsWith("Google Drive",StringComparison.OrdinalIgnoreCase) || strPart.EndsWith(".icloud",StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Choose a picture outside cloud storage.");
            strCurrent=Path.Combine(strCurrent,strPart);
            if((File.GetAttributes(strCurrent)&(FileAttributes.ReparsePoint|FileAttributes.Offline|(FileAttributes)0x440000))!=0)
                throw new ArgumentException("Choose a downloaded local picture without links.");
        }
        return strFull;
    }
}
