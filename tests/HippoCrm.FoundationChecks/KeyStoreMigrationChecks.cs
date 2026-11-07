using System.Security.Cryptography;
using HippoCrm.Core;

internal static class KeyStoreMigrationChecks
{
    internal static void Run()
    {
        var iChecks = 0;
        using var objSource = new ProbeStore();
        using var objTarget = new ProbeStore { arrStored = null };
        KeyStoreMigration.CopyVerifiedKey(objSource, objTarget, "fictional-workspace");
        Check(objSource.arrStored!.SequenceEqual(objTarget.arrStored!), "Migration preserves the original key exactly");
        Check(objSource.arrReturned!.All(bytValue => bytValue == 0) && objTarget.arrReturned!.All(bytValue => bytValue == 0), "Migration wipes owned read buffers after verification");
        KeyStoreMigration.CopyVerifiedKey(objSource, objTarget, "fictional-workspace");
        Check(objSource.arrStored!.SequenceEqual(objTarget.arrStored!), "Repeating migration is idempotent");
        objTarget.arrStored![0] ^= 1;
        Reject(() => KeyStoreMigration.CopyVerifiedKey(objSource, objTarget, "fictional-workspace"), "Conflicting destination is rejected without replacement");
        Check(objSource.arrStored![0] != objTarget.arrStored![0] && objSource.arrReturned!.All(bytValue => bytValue == 0), "Conflict preserves both stores and clears the source read");
        objTarget.bDeny = true;
        Reject(() => KeyStoreMigration.CopyVerifiedKey(objSource, objTarget, "fictional-workspace"), "Permission denial never falls back to a file or creates a new key");
        objTarget.bDeny = false; objTarget.arrStored![0] ^= 1; objTarget.bCorruptRead = true;
        Reject(() => KeyStoreMigration.CopyVerifiedKey(objSource, objTarget, "fictional-workspace"), "Corrupted readback prevents a successful migration result");
        Check(objTarget.arrReturned!.All(bytValue => bytValue == 0), "Failed verification clears readback buffers");
        SensitiveMemory.Clear(objSource.arrStored); objSource.arrStored = new byte[31];
        Reject(() => KeyStoreMigration.CopyVerifiedKey(objSource, objTarget, "fictional-workspace"), "Malformed source keys are rejected");
        SensitiveMemory.Clear(objSource.arrStored); objSource.arrStored = null;
        Reject(() => KeyStoreMigration.CopyVerifiedKey(objSource, objTarget, "fictional-workspace"), "Missing source keys are never regenerated");
        Console.WriteLine($"{iChecks} key migration checks passed (isolated adapters; not native macOS acceptance).");
        void Check(bool bCondition, string strMessage) { if (!bCondition) throw new InvalidOperationException("FAIL " + strMessage); iChecks++; Console.WriteLine("PASS " + strMessage); }
        void Reject(Action actOperation, string strMessage) { try { actOperation(); } catch (CryptographicException) { Check(true, strMessage); return; } throw new InvalidOperationException("FAIL " + strMessage); }
    }

    internal static void Native()
    {
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException("Run the Keychain acceptance check on macOS.");
        var strIdentity = Path.Combine(Path.GetTempPath(), "hippocrm-keychain-check-" + Guid.NewGuid().ToString("N"));
        var objStore = new MacKeychainKeyStore();
        var arrKey = RandomNumberGenerator.GetBytes(32);
        byte[]? arrRead = null;
        try
        {
            objStore.ImportKey(strIdentity, arrKey);
            objStore.ImportKey(strIdentity, arrKey);
            arrRead = objStore.ReadKey(strIdentity);
            if (!CryptographicOperations.FixedTimeEquals(arrKey, arrRead)) throw new CryptographicException("Keychain readback failed.");
            arrKey[0] ^= 1;
            try { objStore.ImportKey(strIdentity, arrKey); }
            catch (CryptographicException) { Console.WriteLine("PASS Native Keychain import, repeat, readback and conflict rejection"); return; }
            throw new InvalidOperationException("Conflicting Keychain import was accepted.");
        }
        finally { SensitiveMemory.Clear(arrKey); SensitiveMemory.Clear(arrRead); objStore.DeleteKeyForTesting(strIdentity); }
    }

    private sealed class ProbeStore : IKeyImportStore, IDisposable
    {
        internal byte[]? arrStored = Enumerable.Repeat((byte)37, 32).ToArray();
        internal byte[]? arrReturned;
        internal bool bDeny;
        internal bool bCorruptRead;
        public void EnsureKey(string strWorkspaceIdentity, bool bAllowCreate) => throw new InvalidOperationException("Migration must not generate replacement keys.");
        public byte[] ReadKey(string strWorkspaceIdentity)
        {
            if (bDeny || arrStored is null) throw new CryptographicException("Fixture key unavailable.");
            arrReturned = arrStored.ToArray(); if (bCorruptRead) arrReturned[0] ^= 1; return arrReturned;
        }
        public void ImportKey(string strWorkspaceIdentity, byte[] arrKey)
        {
            if (bDeny) throw new CryptographicException("Fixture access denied.");
            if (arrStored is null) arrStored = arrKey.ToArray();
            else if (!CryptographicOperations.FixedTimeEquals(arrStored, arrKey)) throw new CryptographicException("Fixture key conflict.");
        }
        public void Dispose() { SensitiveMemory.Clear(arrStored); SensitiveMemory.Clear(arrReturned); }
    }
}