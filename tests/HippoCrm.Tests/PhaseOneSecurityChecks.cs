using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using HippoCrm.Core;

internal static class PhaseOneSecurityChecks
{
    private static int iPassed;
    internal static void Run()
    {
        FipsChecks(); NativeMemoryChecks(); ManifestChecks(); UnprovisionedChecks();
        Console.WriteLine($"{iPassed} Phase 1 security checks passed. Provisioned FIPS/macOS acceptance remains pending.");
    }

    private static void FipsChecks()
    {
        HostFipsValidator.VerifyWindowsPolicy(0, 1); Check(true, "Enabled Windows host policy is accepted");
        Reject<CryptographicException>(() => HostFipsValidator.VerifyWindowsPolicy(0, 0), "Disabled host mode fails closed");
        Reject<CryptographicException>(() => HostFipsValidator.VerifyWindowsPolicy(-1, 1), "Host policy API error fails closed");
        var objFips = new HostFipsValidator();
        objFips.VerifyModuleMode(1L); Check(true, "SQLCipher INTEGER 1 is accepted");
        foreach (var objStatus in new object?[] { null, DBNull.Value, 0L, 2L, -1L, "1", true, 1 })
            Reject<CryptographicException>(() => objFips.VerifyModuleMode(objStatus), "Missing, disabled or malformed module status fails closed");
        var lstCalls = new List<string>();
        var objManifest = new ProbeManifest(() => lstCalls.Add("manifest"));
        var objHost = new ProbeFips(() => lstCalls.Add("host"));
        new StartupSecurityGate(objManifest, objHost).Verify();
        Check(lstCalls.SequenceEqual(new[] { "host", "manifest" }), "Host policy is checked before manifest cryptography");
        lstCalls.Clear();
        Reject<CryptographicException>(() => new StartupSecurityGate(new ProbeManifest(() => throw new CryptographicException()), objHost).Verify(), "Manifest rejection aborts startup");
        Check(lstCalls.SequenceEqual(new[] { "host" }), "Manifest failure cannot complete startup");
        lstCalls.Clear();
        Reject<CryptographicException>(() => new StartupSecurityGate(objManifest, new ProbeFips(() => throw new CryptographicException())).Verify(), "Host rejection aborts startup");
        Check(lstCalls.Count == 0, "Manifest cryptography is unreachable after host failure");
    }

    private static void NativeMemoryChecks()
    {
        var arrSecret = SensitiveMemory.AllocateBytes(32); byte[]? arrFailed = null;
        try
        {
            RandomNumberGenerator.Fill(arrSecret);
            using var objNative = new NativeSecretBuffer(arrSecret);
            var arrCopy = NativeSecretBuffer.CopyToManaged(objNative.DangerousGetHandle());
            try { Check(arrCopy.SequenceEqual(arrSecret), "Native copy preserves key bytes"); }
            finally { SensitiveMemory.Clear(arrCopy); }
            Reject<IOException>(() => NativeSecretBuffer.CopyToManaged(objNative.DangerousGetHandle(), (ptrSource, arrDestination) =>
            { arrFailed = arrDestination; Marshal.Copy(ptrSource, arrDestination, 0, 16); throw new IOException("Fictional copy failure"); }), "Partial native copy failure propagates");
            Check(arrFailed is not null && arrFailed.All(bytValue => bytValue == 0), "Failed managed key transfer wipes partially copied bytes");
            Reject<CryptographicException>(() => NativeSecretBuffer.CopyToManaged(IntPtr.Zero), "Null native key pointer is rejected");
            var ptrProbe = Marshal.AllocHGlobal(32); Marshal.Copy(arrSecret, 0, ptrProbe, 32);
            var bReleased = false;
            NativeSecretBuffer.ClearAndRelease(ptrProbe, 32, ptrReleasing =>
            {
                try
                {
                    var arrObserved = new byte[32]; Marshal.Copy(ptrReleasing, arrObserved, 0, 32);
                    Check(arrObserved.All(bytValue => bytValue == 0), "Native bytes are zero before release"); bReleased = true;
                }
                finally { Marshal.FreeHGlobal(ptrReleasing); }
            });
            Check(bReleased, "Native release follows zeroization");
            objNative.Dispose(); objNative.Dispose(); Check(objNative.IsClosed, "Secret owner disposal is idempotent");
        }
        finally { SensitiveMemory.Clear(arrSecret); SensitiveMemory.Clear(arrFailed); }
    }

    private static void ManifestChecks()
    {
        // These files contain fictional public assets only; no native fixture is loaded.
        var strRoot = Path.GetFullPath(Path.Combine("artifacts", "phase1-checks", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(strRoot);
        using var objSigner = RSA.Create(3072); using var objOtherSigner = RSA.Create(3072);
        var arrPublicKey = objSigner.ExportSubjectPublicKeyInfo();
        var strExecutable = Path.Combine(strRoot, "HippoCrm.exe");
        var strNativeName = OperatingSystem.IsWindows() ? "e_sqlcipher.dll" : "libe_sqlcipher.dylib";
        var strNative = Path.Combine(strRoot, strNativeName); var strAsset = Path.Combine(strRoot, "font.bin");
        foreach (var strPath in new[] { strExecutable, strNative, strAsset }) File.WriteAllText(strPath, "FICTIONAL public asset, never executed");
        var strPlatform = (OperatingSystem.IsWindows() ? "win-" : "osx-") + RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
        var objManifest = new ReleaseManifestVerifier.ReleaseManifest
        {
            iVersion = 1, strPlatform = strPlatform, strModuleId = "FICTIONAL MODULE", strValidationCertificate = "FICTIONAL NOT A CERTIFICATE",
            strSqlCipherPath = strNativeName,
            lstFiles = new[] { strExecutable, strNative, strAsset }.Select(strPath => (ReleaseManifestVerifier.ReleaseFile?)new()
            { strPath = Path.GetFileName(strPath), strSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(strPath))) }).ToList()
        };
        void Sign()
        {
            var arrManifest = JsonSerializer.SerializeToUtf8Bytes(objManifest);
            File.WriteAllBytes(Path.Combine(strRoot, ReleaseManifestVerifier.strManifestName), arrManifest);
            File.WriteAllBytes(Path.Combine(strRoot, ReleaseManifestVerifier.strSignatureName), objSigner.SignData(arrManifest, HashAlgorithmName.SHA256, RSASignaturePadding.Pss));
        }
        string Verify() => ReleaseManifestVerifier.VerifyDirectory(strRoot, strExecutable, arrPublicKey, [strAsset]);
        Sign(); Check(Verify() == strNative, "Complete signed fictional inventory verifies");
        Reject<CryptographicException>(() => ReleaseManifestVerifier.VerifyDirectory(strRoot, strExecutable, objOtherSigner.ExportSubjectPublicKeyInfo(), [strAsset]), "Untrusted replacement key cannot authenticate manifest");
        foreach (var strPath in new[] { strExecutable, strNative, strAsset })
        {
            var arrOriginal = File.ReadAllBytes(strPath); File.AppendAllText(strPath, "tampered");
            Reject<CryptographicException>(() => Verify(), "Tampered executable, native module or asset is rejected"); File.WriteAllBytes(strPath, arrOriginal);
        }
        File.AppendAllText(Path.Combine(strRoot, ReleaseManifestVerifier.strManifestName), " ");
        Reject<CryptographicException>(() => Verify(), "Manifest byte tampering invalidates signature"); Sign();
        File.WriteAllBytes(Path.Combine(strRoot, ReleaseManifestVerifier.strSignatureName), [1, 2, 3]);
        Reject<CryptographicException>(() => Verify(), "Malformed signature fails closed"); Sign();
        using (var stmLocked = new FileStream(strAsset, FileMode.Open, FileAccess.Read, FileShare.None))
            Reject<CryptographicException>(() => Verify(), "Locked release file fails closed");
        var strOriginalPath = objManifest.lstFiles![2]!.strPath;
        foreach (var strInvalid in new[] { "../escape", "C:/escape", "/escape", "sub\\escape", "sub/./escape", "file:stream" })
        {
            objManifest.lstFiles[2]!.strPath = strInvalid; Sign();
            Reject<CryptographicException>(() => Verify(), "Signed traversal or alternate-stream path is rejected");
        }
        objManifest.lstFiles[2]!.strPath = strOriginalPath;
        var strOriginalHash = objManifest.lstFiles[2]!.strSha256;
        objManifest.lstFiles[2]!.strSha256 = new string('z', 64); Sign();
        Reject<CryptographicException>(() => Verify(), "Non-hexadecimal digest is safely rejected");
        objManifest.lstFiles[2]!.strSha256 = strOriginalHash;
        objManifest.lstFiles.Add(objManifest.lstFiles[2]); Sign();
        Reject<CryptographicException>(() => Verify(), "Duplicate inventory path is rejected"); objManifest.lstFiles.RemoveAt(3);
        objManifest.lstFiles.RemoveAt(2); Sign();
        Reject<CryptographicException>(() => Verify(), "Uncovered required asset is rejected");
        objManifest.lstFiles.Add(new() { strPath = strOriginalPath, strSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(strAsset))) });
        objManifest.strPlatform = "wrong-platform"; Sign();
        Reject<CryptographicException>(() => Verify(), "Wrong platform is rejected"); objManifest.strPlatform = strPlatform;
        objManifest.strModuleId = ""; Sign();
        Reject<CryptographicException>(() => Verify(), "Missing approved module identity is rejected"); objManifest.strModuleId = "FICTIONAL MODULE";
        objManifest.lstFiles.Add(new() { strPath = "missing.bin", strSha256 = new string('0', 64) }); Sign();
        Reject<CryptographicException>(() => Verify(), "Missing release file is rejected"); objManifest.lstFiles.RemoveAt(3); Sign();
        File.WriteAllBytes(Path.Combine(strRoot, ReleaseManifestVerifier.strManifestName), new byte[4 * 1024 * 1024 + 1]);
        Reject<CryptographicException>(() => Verify(), "Oversized manifest is rejected before signature parsing"); Sign();
        File.WriteAllBytes(Path.Combine(strRoot, ReleaseManifestVerifier.strSignatureName), []);
        Reject<CryptographicException>(() => Verify(), "Empty signature is rejected"); Sign();
        var strMissingRoot = Path.GetFullPath(Path.Combine("artifacts", "phase1-missing", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(strMissingRoot);
        Reject<CryptographicException>(() => ReleaseManifestVerifier.VerifyDirectory(strMissingRoot,
            Path.Combine(strMissingRoot, "HippoCrm.exe"), arrPublicKey, []), "Missing manifest fails closed");
        File.Copy(Path.Combine(strRoot, ReleaseManifestVerifier.strManifestName), Path.Combine(strMissingRoot, ReleaseManifestVerifier.strManifestName));
        Reject<CryptographicException>(() => ReleaseManifestVerifier.VerifyDirectory(strMissingRoot,
            Path.Combine(strMissingRoot, "HippoCrm.exe"), arrPublicKey, []), "Missing detached signature fails closed");
        File.WriteAllText(Path.Combine(strRoot, "injected.dll"), "FICTIONAL unlisted file");
        Reject<CryptographicException>(() => Verify(), "Unlisted injected library is rejected");
    }

    private static void UnprovisionedChecks()
    {
        Reject<CryptographicException>(() => new ReleaseManifestVerifier().Verify(), "Missing compiled release trust anchor fails closed");
        Reject<CryptographicException>(StartupSecurity.Verify, "Unprovisioned production startup is denied");
        var objKeys = new RejectingKeyStore();
        var strRoot = Path.GetFullPath(Path.Combine("artifacts", "phase1-denied", Guid.NewGuid().ToString("N")));
        Reject<CryptographicException>(() => new Database(Path.Combine(strRoot, "crm.db"), objKeys), "Direct Core workspace construction cannot bypass gate");
        Check(!Directory.Exists(strRoot) && objKeys.iCalls == 0, "Denied startup creates no workspace and never accesses keys");
        Reject<CryptographicException>(() => SqlCipherConnectionFactory.Open(Path.Combine(strRoot, "crm.db"), objKeys, true), "Direct connection factory cannot bypass gate");
        Check(!Directory.Exists(strRoot) && objKeys.iCalls == 0, "Denied connection never creates files or credentials");
    }

    private static void Check(bool bCondition, string strMessage)
    { if (!bCondition) throw new InvalidOperationException("FAIL " + strMessage); iPassed++; Console.WriteLine("PASS " + strMessage); }
    private static void Reject<T>(Action actCheck, string strMessage) where T : Exception
    { try { actCheck(); } catch (T) { Check(true, strMessage); return; } throw new InvalidOperationException("FAIL " + strMessage); }
    private sealed class ProbeManifest(Action actVerify) : IManifestVerifier { public void Verify() => actVerify(); }
    private sealed class ProbeFips(Action actVerify) : IFipsValidator
    { public void VerifyHostPolicy() => actVerify(); public void VerifyModuleMode(object? objStatus) => throw new NotSupportedException(); }
    private sealed class RejectingKeyStore : IDatabaseKeyStore
    {
        internal int iCalls;
        public void EnsureKey(string strPath, bool bCreate) { iCalls++; throw new InvalidOperationException("Must never touch keys."); }
        public byte[] ReadKey(string strPath) { iCalls++; throw new InvalidOperationException("Must never touch keys."); }
    }
}
