# Step 2: access control, transactional security auditing, and password buffers

Step 2 is implemented in source. No Step 3 background execution or inactivity-timer replacement is included. No external packages were added or downloaded in this step. The Step 1 SQLCipher/provider availability and full-desktop validation limitations remain in effect.

## Authorization policy

| Role | Record access | Mutation permissions | User/permission administration |
| --- | --- | --- | --- |
| Administrator | All contacts and organizations | Create/update/archive profiles and append/end related history | Create users, change roles, grant/revoke individual record access |
| Editor | Only explicitly granted records | Update a granted profile and its history only when its grant permits writing | None |
| Viewer | Only explicitly granted records | None, even if a grant contains can_write=1 | None |

New users default to no record grants. Only administrators can create CRM profiles. Access to a contact does not expose an unassigned employer's identity: employment entries also require organization visibility. Adding/ending employment requires contact write permission and organization read permission. Lists, search and dashboard aggregates are scoped to the authenticated user's grants.

`AccessControl.cs` checks current database roles, enabled status and grants in the same transaction as the operation. `CrmService.cs` enforces these checks for every data API. Controls are supplementary restrictions; calling a command/service directly cannot bypass authorization. After a denial, the view model clears cached workspace state and returns to sign-in. It does not yet proactively poll other processes for permission changes; an already displayed snapshot is invalidated on the next operation or lock. Background scheduling remains Step 3.

`Session.cs` replaces publicly constructible identity records with internally issued, random 256-bit credentials. Only token hashes are persisted. Identity display fields are never accepted as authentication evidence. Session credentials are pinned, zeroed on disposal, and revoked transactionally on sign-out. Role changes revoke that user's existing sessions; subsequent access must re-authenticate. The last enabled administrator cannot be demoted.

`UserAdministrationService.cs` provides backend APIs (`CreateUser`, `SetRole`, `SetRecordAccess`). An administration screen is not included in this step. There are no built-in additional-user passwords or recovery credentials.

## Audit behavior

`SecurityAudit.cs` accepts typed event/entity enums and numeric actor/subject/record identifiers. Usernames, submitted unknown identifiers, password/hash/key buffers and CRM field bodies are not event payloads. Unknown or malformed authentication attempts use a nullable actor ID.

- Administrator bootstrap, session issuance and successful sign-in auditing commit together.
- Password-failure counters/lockout state and their audit event commit together. Immediate transactions serialize the read/increment/update sequence across concurrent connections.
- Unknown-account, locked-account and setup-rejection events are persisted before the authentication error is returned.
- CRM changes, user provisioning, role changes and record-grant changes commit with their audit event.
- Permission denials persist a denial event before throwing. They occur before any mutation.
- Authorized CRM read operations also require an `AccessVerified` event before returning, so audit-write failure blocks sensitive reads as well as writes. These events identify the actor and service access; they do not enumerate every record displayed.
- SQL failures roll back the whole transaction. Audit failure never grants a session or returns sensitive query results.
- Audit update/delete triggers remain enforced during ordinary database use. This does not make a writable database tamper-proof against a user who can replace its schema outside the application.

If sign-out auditing fails, its database transaction fails, but the local session credential is still zeroed and UI state is cleared. No unlogged sign-out success is claimed. Orphaned session hashes cannot recreate the disposed random credential; session expiry/cleanup remains part of Step 3.

## Password ownership and UI

`AuthService` accepts `SecureString`, with no immutable password-string overload. `PasswordBuffer.cs` snapshots the caller's SecureString, makes the copy read-only, converts it through a short-lived unmanaged UTF-16 buffer and pinned character/UTF-8 arrays, and clears/frees those buffers in finally blocks. Derived hash buffers are pinned and cleared by the authentication caller after use. PBKDF2-SHA256 remains 600,000 iterations with 32-byte random salts and fixed-time comparisons, preserving existing valid password hashes.

Callers own/dispose the input SecureString; authentication does not unexpectedly dispose caller-owned objects. The workspace replaces and disposes both password/confirmation buffers after every submission, including failures, and on lock/close.

`SecurePasswordInput.cs` keeps only bullet characters in TextBox.Text. Keyboard text is inserted directly into the SecureString; plaintext is not data-bound or stored in the TextBox undo history. The underlying TextBox is read-only to prevent built-in clipboard paste paths, and backspace/delete/selection are handled explicitly. Clipboard paste is therefore unavailable in this implementation. Real Avalonia keyboard, IME, automation/screen-reader and accessibility acceptance remains pending.

The framework/OS owns transient immutable text-input event strings. This code avoids retaining them; it cannot promise that the runtime can forcibly erase every framework-owned string or OS input buffer. SecureString is not a guarantee that all process/OS memory is unrecoverable.

## Schema transition

`SchemaManager.cs` runs `Migration002.sql` inside the existing encrypted-database initialization transaction. Version 2 adds roles, enabled status, session hashes, per-user contact/organization grants and nullable audit actor/subject identifiers.

SQLite requires rebuilding the audit table to remove its NOT NULL actor constraint. All old rows/IDs/payloads are copied unchanged within the same transaction, retention triggers are restored, and a SchemaUpgraded event is appended before commit. The single version-1 bootstrap administrator keeps its documented administrator role. Databases containing multiple legacy users require explicit role mapping and are rejected without partial migration. Unsupported versions and nonempty unversioned databases are rejected.

This does not convert plaintext files: the Step 1 plaintext rejection remains. No real application database was opened or migrated during implementation.

## Automated verification

The dependency-free `tests/HippoCrm.SecurityChecks` project source-links the actual production authentication, authorization, CRM, administration, schema-manager and view-model code. It executes their SQL against the Windows-bundled SQLite engine in isolated in-memory databases through a test-only ADO surface adapter. That adapter is never referenced by the production app and provides no production plaintext fallback. It requires Windows and does not validate SQLCipher, the Microsoft.Data.Sqlite package ABI, or encrypted connection setup.

Checks cover the role/grant matrix, guessed IDs, hidden employer metadata, filtered aggregates/search, forged credentials, immediate grant revocation, role/session revocation, lockout/audit behavior, audit-write rollback for reads/authentication/records/users/permissions, append-only triggers, migration preservation/rejection, SecureString disposal and cached UI clearing. The same policy-check source is linked into the encrypted integration suite, ready to run with the verified SQLCipher provider.

The foundation suite additionally verifies source syntax/Hungarian declarations, architecture contracts, UTF-8/Unicode password derivation, secure-password equality, length rejection, session uniqueness/non-public construction, original token-buffer clearing and disposal behavior.

Run the locally available checks from the repository root:

```powershell
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
dotnet restore tests/HippoCrm.SecurityChecks --configfile NuGet.Config
dotnet restore tests/HippoCrm.FoundationChecks --configfile NuGet.Config
dotnet run --project tests/HippoCrm.SecurityChecks --configuration Release --no-restore
dotnet run --project tests/HippoCrm.FoundationChecks --configuration Release --no-restore
```

Both projects use installed SDK/OS components and declare no NuGet package dependencies. Real SQLCipher/provider integration, the complete Avalonia UI suite and tests with network adapters disabled are still blocked/pending as documented in Step 1. No network adapters were changed on this machine.

Stop here for approval before Step 3.
