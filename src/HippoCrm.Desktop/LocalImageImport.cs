using System.Security.Cryptography;

namespace HippoCrm.Desktop;

public static class LocalImageImport
{
    private sealed class LocalImageImportException(string strMessage) : ArgumentException(strMessage) { }
    public const int iMaximumSourceBytes=10*1024*1024;
    public static async Task<byte[]> ReadAsync(string strPath,CancellationToken ctCancellation=default)
    {
        byte[]? arrInput=null;
        try
        {
            ctCancellation.ThrowIfCancellationRequested();
            var strLocal=ValidateLocalPath(strPath);
            await using var stmSource=new FileStream(strLocal,FileMode.Open,FileAccess.Read,FileShare.Read,81920,FileOptions.Asynchronous|FileOptions.SequentialScan);
            if(stmSource.Length is < 8 or > iMaximumSourceBytes) throw new LocalImageImportException("Choose a PNG or JPEG picture under 10 MB.");
            arrInput=GC.AllocateArray<byte>(checked((int)stmSource.Length),pinned:true);
            await stmSource.ReadExactlyAsync(arrInput,ctCancellation);
            ctCancellation.ThrowIfCancellationRequested();
            // Recheck path attributes after reading; never retain source paths in CRM data.
            ValidateLocalPath(strLocal);
            var arrResult=arrInput; arrInput=null; return arrResult;
        }
        catch(OperationCanceledException) { throw; }
        catch(LocalImageImportException) { throw; }
        catch(UnauthorizedAccessException)
        { throw new ArgumentException("The picture could not be opened because local file access was denied. Choose a picture you have permission to read."); }
        catch(IOException)
        { throw new ArgumentException("The picture could not be read. Close any app using it and try a downloaded local copy."); }
        catch(Exception objException) when(objException is ArgumentException or NotSupportedException)
        { throw new ArgumentException("Choose a valid local PNG or JPEG picture under 10 MB."); }
        finally { if(arrInput is not null) CryptographicOperations.ZeroMemory(arrInput); }
    }
    public static string ValidateLocalPath(string strPath)
    {
        if(!Path.IsPathFullyQualified(strPath) || strPath.StartsWith(@"\\",StringComparison.Ordinal) || strPath.Contains("://",StringComparison.Ordinal))
            throw new LocalImageImportException("Choose a fully local picture.");
        var strFull=Path.GetFullPath(strPath);
        if (OperatingSystem.IsMacOS())
        {
            // DriveInfo.GetDrives can block while macOS probes mounted or unavailable
            // volumes. Imports are intentionally limited to the local data volume;
            // users can copy removable/network images to a local folder first.
            if (strFull.StartsWith("/Volumes/", StringComparison.Ordinal)
                || strFull.StartsWith("/Network/", StringComparison.Ordinal))
                throw new LocalImageImportException("Copy the picture to a local folder on this Mac before importing it.");
        }
        else
        {
            var objMount=DriveInfo.GetDrives().Where(objDrive=>strFull.StartsWith(objDrive.Name.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)+Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(objDrive=>objDrive.Name.Length).FirstOrDefault();
            if(objMount is null || objMount.DriveType is not (DriveType.Fixed or DriveType.Removable or DriveType.Ram or DriveType.CDRom))
                throw new LocalImageImportException("Network pictures are not supported.");
        }
        var strCurrent=Path.GetPathRoot(strFull)!;
        foreach(var strPart in strFull[strCurrent.Length..].Split([Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar],StringSplitOptions.RemoveEmptyEntries))
        {
            if(strPart.StartsWith("OneDrive",StringComparison.OrdinalIgnoreCase) || strPart.Equals("CloudStorage",StringComparison.OrdinalIgnoreCase)
                || strPart.Equals("Mobile Documents",StringComparison.OrdinalIgnoreCase) || strPart.Equals("Dropbox",StringComparison.OrdinalIgnoreCase)
                || strPart.StartsWith("GoogleDrive",StringComparison.OrdinalIgnoreCase) || strPart.StartsWith("Google Drive",StringComparison.OrdinalIgnoreCase) || strPart.EndsWith(".icloud",StringComparison.OrdinalIgnoreCase))
                throw new LocalImageImportException("Choose a picture outside cloud storage.");
            strCurrent=Path.Combine(strCurrent,strPart);
            if((File.GetAttributes(strCurrent)&(FileAttributes.ReparsePoint|FileAttributes.Offline|(FileAttributes)0x440000))!=0)
                throw new LocalImageImportException("Choose a downloaded local picture without links.");
        }
        return strFull;
    }
}
