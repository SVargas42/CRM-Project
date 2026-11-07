# macOS Keychain migration readiness

## Current state

The native Keychain adapter and an add-only verified key-copy operation are available for acceptance testing. They are not selected by the desktop workspace yet. Existing Windows Credential Manager behavior and the macOS demo file-key behavior are unchanged. This increment does not resolve the production key-storage finding on its own.

`KeyStoreMigration.CopyVerifiedKey` reads one existing key, imports it without replacing a conflicting destination, verifies readback in constant time, and wipes its owned read buffers on success or failure. It never creates a replacement source key, rewrites a database, or removes the original key. Both database and audit-master identities must be handled together before switching a workspace. A partial copy can be retried; it is not evidence that the whole workspace has migrated.

## Verified locally

The automated adapter checks cover exact copy, idempotence, conflicting keys, access denial, corrupted readback, missing/malformed source keys, and owned-buffer cleanup. The native adapter uses the existing project-owned native buffer implementation; no packages, external calls, credentials or new encryption algorithms are introduced.

## Required Mac acceptance

With the existing approved offline restore complete, run:

```sh
dotnet build tests/HippoCrm.FoundationChecks --configuration Debug --no-restore
dotnet run --project tests/HippoCrm.FoundationChecks --configuration Debug --no-build --no-restore -- --mac-keychain
```

The explicit check uses a random fictional Keychain identity, verifies import/readback/repeat/conflict rejection, and removes only its test item. It does not read existing workspace keys. macOS may request Keychain authorization; cancellation must fail without falling back to raw files. This command validates native calls, not the signing identity or prompt behavior of the desktop app.

Before selecting Keychain in the desktop, also verify a stable signed HippoCRM app bundle on the target Mac, approval cancellation, locked Keychain, restart, both database/audit keys, and successful reopening of a disposable migrated workspace including photos and interaction history. Repeat normal navigation to check that authorization prompts do not recur. No conclusion about prompt frequency can be made from Windows adapter tests.

## Recovery and cleanup boundary

Do not delete original file keys until both identities and complete database/audit reopening are independently verified. The original encrypted database and audit/checkpoint files must remain together. Key migration is not a backup/restore implementation. Any later key-file retirement needs an explicit, tested cleanup step; overwriting a file cannot guarantee erasure from SSD snapshots or historical backups. Until retirement is completed, the raw-key production finding remains open.

Do not distribute real keys, database files, local logs or checkpoint files through GitHub. Do not bypass production startup security checks to perform migration.