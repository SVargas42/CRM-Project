using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HippoCrm.Core;

// Detached signature authenticates the exact UTF-8 manifest bytes. Trust comes
// from a build-embedded public key, never an adjacent replaceable certificate.
internal sealed class ReleaseManifestVerifier : IManifestVerifier
{
    internal const string strManifestName = "hippocrm-release.json";
    internal const string strSignatureName = "hippocrm-release.sig";
    private const int iMaximumManifestBytes = 4 * 1024 * 1024;
    private static readonly JsonSerializerOptions objJsonOptions = new()
    { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, PropertyNameCaseInsensitive = false, MaxDepth = 8 };

    public void Verify()
    {
        _ = VerifyInstallation();
    }

    internal string VerifyInstallation()
    {
        using var stmKey = typeof(ReleaseManifestVerifier).Assembly.GetManifestResourceStream("HippoCrm.ReleaseSigningKey.spki")
            ?? throw new CryptographicException("The approved release verification key has not been provisioned.");
        if (stmKey.Length is < 1 or > 2048) throw new CryptographicException("The release verification key is invalid.");
        var arrKey = new byte[checked((int)stmKey.Length)];
        stmKey.ReadExactly(arrKey);
        // Extracted single-file libraries are not yet a provisioned deployment
        // layout. Refuse that layout instead of authenticating unrelated files.
        var strCorePath = typeof(ReleaseManifestVerifier).Assembly.Location;
        var strEntryAssembly = Assembly.GetEntryAssembly()?.Location;
        if (string.IsNullOrEmpty(strCorePath) || string.IsNullOrEmpty(strEntryAssembly))
            throw new CryptographicException("This release layout has not been approved.");
        return VerifyDirectory(AppContext.BaseDirectory, Environment.ProcessPath ?? "", arrKey,
            [strCorePath, strEntryAssembly]);
    }

    internal static string VerifyDirectory(string strRoot, string strExecutable, byte[] arrPublicKey,
        IReadOnlyCollection<string> lstRequiredFiles)
    {
        try
        {
            strRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(strRoot));
            RejectAliases(strRoot);
            var arrManifest = ReadBounded(Path.Combine(strRoot, strManifestName), iMaximumManifestBytes);
            var arrSignature = ReadBounded(Path.Combine(strRoot, strSignatureName), 1024);
            using var objRsa = RSA.Create();
            objRsa.ImportSubjectPublicKeyInfo(arrPublicKey, out var iConsumed);
            if (iConsumed != arrPublicKey.Length || objRsa.KeySize < 3072 ||
                !objRsa.VerifyData(arrManifest, arrSignature, HashAlgorithmName.SHA256, RSASignaturePadding.Pss))
                throw new CryptographicException("The release manifest could not be authenticated.");
            var objManifest = JsonSerializer.Deserialize<ReleaseManifest>(arrManifest, objJsonOptions)
                ?? throw new CryptographicException("The release manifest is invalid.");
            var strPlatform = (OperatingSystem.IsWindows() ? "win-" : OperatingSystem.IsMacOS() ? "osx-" : "unsupported-")
                + RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
            if (objManifest.iVersion != 1 || objManifest.strPlatform != strPlatform ||
                string.IsNullOrWhiteSpace(objManifest.strModuleId) || string.IsNullOrWhiteSpace(objManifest.strValidationCertificate) ||
                objManifest.lstFiles is null || objManifest.lstFiles.Count is < 1 or > 10000)
                throw new CryptographicException("The release policy is missing or does not match this platform.");
            var setExpected = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            foreach (var objFile in objManifest.lstFiles)
            {
                if (objFile is null || objFile.strSha256 is null || objFile.strSha256.Length != 64)
                    throw new CryptographicException("The release inventory is invalid.");
                var strPath = ResolveFile(strRoot, objFile.strPath);
                if (!setExpected.Add(strPath) || Path.GetFileName(strPath) is strManifestName or strSignatureName)
                    throw new CryptographicException("The release inventory contains a duplicate or reserved file.");
                using var stmFile = new FileStream(strPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(stmFile), Convert.FromHexString(objFile.strSha256)))
                    throw new CryptographicException("A release file failed integrity verification.");
            }
            var strNativePath = ResolveFile(strRoot, objManifest.strSqlCipherPath);
            var strNativeName = OperatingSystem.IsWindows() ? "e_sqlcipher.dll" : "libe_sqlcipher.dylib";
            if (Path.GetFileName(strNativePath) != strNativeName || !setExpected.Contains(strNativePath) ||
                !setExpected.Contains(ResolveFile(strRoot, Path.GetRelativePath(strRoot, strExecutable))))
                throw new CryptographicException("The executable or approved encryption module is not covered by the release manifest.");
            foreach (var strRequired in lstRequiredFiles)
                if (!setExpected.Contains(ResolveFile(strRoot, Path.GetRelativePath(strRoot, strRequired))))
                    throw new CryptographicException("A required application assembly is not covered by the release manifest.");
            VerifyInventory(strRoot, strRoot, setExpected);
            return strNativePath;
        }
        catch (Exception objException) when (objException is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException or FormatException)
        { throw new CryptographicException("The release installation could not be verified.", objException); }
    }

    // Use explicit recursion so links are rejected before enumerating children.
    private static void VerifyInventory(string strRoot, string strDirectory, HashSet<string> setExpected)
    {
        foreach (var strPath in Directory.EnumerateFileSystemEntries(strDirectory))
        {
            if ((File.GetAttributes(strPath) & FileAttributes.ReparsePoint) != 0)
                throw new CryptographicException("Release aliases are prohibited.");
            if (Directory.Exists(strPath)) VerifyInventory(strRoot, strPath, setExpected);
            else if (!setExpected.Contains(strPath) && strPath != Path.Combine(strRoot, strManifestName)
                && strPath != Path.Combine(strRoot, strSignatureName))
                throw new CryptographicException("An unlisted file is present in the release installation.");
        }
    }

    private static byte[] ReadBounded(string strPath, int iMaximumBytes)
    {
        RejectAliases(strPath);
        using var stmFile = new FileStream(strPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stmFile.Length < 1 || stmFile.Length > iMaximumBytes) throw new CryptographicException("A release verification file is invalid.");
        var arrBytes = new byte[checked((int)stmFile.Length)]; stmFile.ReadExactly(arrBytes); return arrBytes;
    }

    private static string ResolveFile(string strRoot, string? strRelative)
    {
        if (string.IsNullOrWhiteSpace(strRelative) || strRelative.Contains('\\') || strRelative.Contains(':') ||
            Path.IsPathRooted(strRelative) || strRelative.Split('/').Any(strPart => strPart is "" or "." or ".." || strPart.EndsWith(' ') || strPart.EndsWith('.')))
            throw new CryptographicException("The release inventory contains an invalid path.");
        var strPath = Path.GetFullPath(Path.Combine(strRoot, strRelative));
        if (!strPath.StartsWith(strRoot + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new CryptographicException("A release file is outside the installation.");
        RejectAliases(strPath); return strPath;
    }

    private static void RejectAliases(string strPath)
    {
        var lstAncestors = new List<string>();
        for (var objPath = new FileInfo(strPath) as FileSystemInfo; objPath is not null;
            objPath = Directory.GetParent(objPath.FullName))
            lstAncestors.Add(objPath.FullName);
        // Inspect the root first: probing a child before rejecting a parent
        // junction could initiate access to a remote filesystem.
        foreach (var strAncestor in lstAncestors.AsEnumerable().Reverse())
            if ((File.GetAttributes(strAncestor) & FileAttributes.ReparsePoint) != 0)
                throw new CryptographicException("Release aliases are prohibited.");
    }

    internal sealed class ReleaseManifest
    {
        public int iVersion { get; set; }
        public string? strPlatform { get; set; }
        public string? strModuleId { get; set; }
        public string? strValidationCertificate { get; set; }
        public string? strSqlCipherPath { get; set; }
        public List<ReleaseFile?>? lstFiles { get; set; }
    }
    internal sealed class ReleaseFile
    {
        public string? strPath { get; set; }
        public string? strSha256 { get; set; }
    }
}
