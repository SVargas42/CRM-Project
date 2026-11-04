# Phase 3 security review: F04 and F07

Status: source changes prepared for manual review; local commits require code approval. No Git commands or network synchronization were used in this phase.

## F04: monotonic session expiry

`Session` owns the inactivity deadline. Accepted activity renews it only after checking expiry under the same credential lock. At exactly 15 minutes the session expires, and expiration remains latched. Authentication checks do not count as user activity. A regressing or unavailable clock fails closed.

The production clock uses Windows `GetTickCount64` or macOS `mach_continuous_time` with its timebase conversion. Both include suspended time; civil-clock changes do not extend the deadline. A one-shot timer signals expiration independently of the 30-second dispatcher fallback. The view posts that signal onto the UI dispatcher; credential checks remain authoritative even if the dispatcher is delayed. No UI can repaint while its dispatcher is blocked, but queued input and service calls cannot revive the expired session.

Window class handlers check pointer movement/press/release/wheel, key down/up and text input during tunneling, including input in owned modal windows. Expired input is marked handled and starts locking before controls accept it. Window activation checks expiry. The main authenticated command dispatcher independently checks expiry. Locking invalidates the credential immediately, before awaiting an outstanding operation or confirmation. Service identity checks and EF command guards enforce expiration independently of UI delivery. CRM operations recheck the credential after their work, so expiry at that point rolls the transaction back instead of committing a denial alongside a mutation.

An internal revocation-only capability allows sign-out to retire an expired credential without granting business access. It still resolves the live token and user, updates retained session status, and audits the revocation in the same coordinated transaction. Disposal clears the pinned token and disposes the deadline timer.

Focused behavioral diff (the implementation includes synchronization, timer signaling and teardown):

```diff
- RecordActivity() => lastActivity = DateTimeOffset.UtcNow;
- if (DateTimeOffset.UtcNow - lastActivity > fifteenMinutes) Lock();
+ if (!session.TryRecordActivity()) { Lock(); consumeInput = true; }
+ // Inside the credential lock, before changing lastActivity:
+ if (monotonicElapsed >= fifteenMinutes || expired) rejectActivity;
+ // Before authenticated dispatch, and again at the service boundary:
+ if (session.IsExpired) rejectAndLock;
```

## F07: trusted freshness outside database rollback

`TrustedAuditCheckpoint.cs` defines immutable audit heads and versioned checkpoint state, an offline trusted store, and an exclusive per-workspace lease. Production composition uses a build-time partial provisioning hook and defaults to an implementation that throws. There is no adjacent-file, Credential Manager, Keychain, memory-store or development fallback in the production composition. Existing FIPS and authenticated-installation gates remain active.

The client must supply a rollback-resistant hardware or protected local-service adapter. A credential or file that an administrator can restore with the database does not meet this contract. The adapter must authenticate the installation and workspace binding, reject unknown workspaces, serialize access across processes, use bounded contention failures, retain durable monotonic versions across power loss and backups, and implement atomic compare-and-swap. Losing the adapter, its authentication, or its state denies access. Restoring the database and checkpoint together must not be possible within the assumed attacker privilege domain. The application's interface alone cannot establish that physical trust property.

`SecurityAudit` verifies the complete existing HMAC chain, then compares the final signed ID and digest with the trusted checkpoint. It preserves the existing signed root and legacy-history digest. Verification is performed on connection opening and on the first protected action in each transaction. Subsequent appends in that transaction can advance the chain, while the original trusted base remains fixed until commit.

Every production source transaction commit now uses `SecurityAudit.Commit`, including authentication success/denial, user administration, CRM, EF, images, schema initialization and preferences. Theme changes receive a signed audit event so they also advance the checkpoint. Connection opening holds its trusted lease until the connection closes, excluding other cooperating processes across verification, SQLite commit and checkpoint confirmation. SQLite connections explicitly require synchronous FULL; macOS additionally enables and checks fullfsync.

Focused commit diff:

```diff
  appendSignedAuditInSameTransaction();
- transaction.Commit();
+ verifySignedHeadAgainstTrustedBase();
+ checkpoint.CompareExchange(baseVersion, preparedCandidate);
+ transaction.Commit();
+ checkpoint.CompareExchange(preparedVersion, confirmedCandidate);
```

### Crash and rollback behavior

| Trusted state versus verified database | Result |
| --- | --- |
| Confirmed head matches | Access permitted. |
| Confirmed head differs, including a valid shorter suffix or old database snapshot | Recovery required; no new baseline is accepted. |
| Prepare write fails before becoming durable | SQLite commit is never invoked; transaction disposal rolls back the mutation and audit. |
| Prepared candidate matches the database | The database commit is proven by its authenticated chain; confirmation completes automatically with a new trusted version. Recovery is idempotent. |
| Prepared candidate does not match, including a database at the prior head | Explicit recovery required; prepared evidence is retained. |
| Protected state unavailable or malformed, or stale version write | Operation rejected. |

A crash before SQLite commit and a hostile rollback after SQLite commit can produce the same prepared checkpoint plus old database. Automatically clearing that prepare would silently accept one of those attacks. The application therefore never does so. It shows a nontechnical recovery-required error and obscures the workspace when detected during an operation.

For this ambiguous case, the provisioner's separately authenticated offline recovery tooling must preserve the original confirmed/prepared states, obtain explicit authorized disposition, and record that disposition in rollback-resistant recovery history. Prefer restoring and verifying the proposed committed database when a trusted recovery image exists. If an authorized investigation determines the operation must be abandoned, any resolution must be an explicit, irreversibly recorded recovery decision with a higher trusted version; it is not an automatic abort or checkpoint reset. This application supplies no reset button, automatic re-enrollment or recovery-authorization bypass. The deployment adapter/recovery tooling remains a client provisioning obligation, so production remains blocked until supplied.

A confirmation failure can occur after SQLite has committed. Callers must not assume an exception means the business mutation was undone; reopen/verify and inspect current records before repeating a mutation. Prepared evidence supports reconciliation without reporting the uncertain operation as successful.

### Enrollment and retained data

There is no application-driven enrollment from whatever database happens to be present. A trusted provisioner must explicitly enroll a new empty workspace or authenticate an existing signed head before enabling it. An anchored version-7 workspace can retain its schema and signed history; no schema migration is introduced by Phase 3. A nonempty workspace without an audit anchor is rejected by production connection opening and requires an independently approved migration/enrollment procedure. Never erase credentials, the database or checkpoint state to clear that rejection. Path moves and alias/case handling must preserve the provisioned workspace identity rather than silently creating another identity.

## Files for review

All paths below are relative to `C:\Users\ellis\Documents\ds3870testGit\CRM-Project`.

| Files | Change |
| --- | --- |
| `src/HippoCrm.Core/Session.cs` | Continuous clock, atomic deadline checks, latched expiry, timer, revocation-only token capability and timer cleanup. |
| `src/HippoCrm.Core/AccessControl.cs` | Freshness and expiry checks before returning live identity; separate revocation identity. |
| `src/HippoCrm.Core/AuthService.cs` | Revokes expired sessions, internal clock injection for deterministic tests, coordinated commits. |
| `src/HippoCrm.Desktop/MainWindow.cs` | Input guards across main/owned windows and UI dispatcher expiration signaling. |
| `src/HippoCrm.Desktop/ViewModels/MainWindowViewModel.cs` | Check-before-refresh/dispatch, immediate invalidation on lock, explicit recovery presentation. |
| `src/HippoCrm.Core/TrustedAuditCheckpoint.cs` (new) | Provisioning interfaces, immutable state and fail-closed prepare/confirm/recovery protocol. |
| `src/HippoCrm.Core/SecurityAudit.cs` | Signed head extraction, per-transaction freshness verification and coordinated commit. |
| `src/HippoCrm.Core/SqlCipherConnectionFactory.cs` | Mandatory trusted lease, open-time verification, enrollment denial, durable SQLite configuration and lease release. |
| `src/HippoCrm.Core/Database.cs` | Provisioning gate before directory/credential creation, coordinated schema/preference transactions and preference auditing. |
| `src/HippoCrm.Core/CrmService.cs`, `CrmDbContext.cs`, `UserAdministrationService.cs`, `ProfileImageService.cs` | Route existing audit-bearing commits through the coordinator; CRM checks expiry after work. |
| `tests/Shared/ManualSessionClock.cs`, `MemoryAuditCheckpoint.cs` (new) | Deterministic one-shot clocks and fault-injected memory-only checkpoint fixtures; not production adapters. |
| `tests/HippoCrm.FoundationChecks/SessionDeadlineChecks.cs`, `TrustedCheckpointChecks.cs` (new), project and `Program.cs` | Boundary/clock/dispatch and checkpoint/crash protocol tests; expanded Hungarian/syntax scan. |
| `tests/HippoCrm.SecurityChecks/PhaseThreeSecurityChecks.cs` (new), `TestDatabase.cs`, project and `Program.cs` | Actual service expiry, revocation, prepare failure and interrupted-confirmation checks in the Windows SQLite adapter. |
| `tests/HippoCrm.EfPolicyChecks/PhaseThreeRollbackChecks.cs` (new), `InMemoryDatabase.cs`, project and `Program.cs` | Real provider/EF expiry, valid suffix truncation, complete snapshot rollback, and shared service tests. |
| `tests/HippoCrm.UiTests/PhaseThreeViewChecks.cs` (new), project and `Program.cs` | Headless exact-boundary/late-input, direct-command and owned-modal checks. |
| `MacPortInfo.txt`, this review | Native clock, adapter provisioning, recovery and acceptance limits. |

## Validation

Automated validation uses only existing local packages and fictional memory-only fixtures. Production startup is never bypassed. The seven-project Debug build passed with zero errors and the two existing ImageSharp project licensing warnings. Executed checks passed:

| Check executable/suite | Passed |
| --- | ---: |
| Foundation syntax/Hungarian/ABI checks | 108 |
| Password/session memory checks | 9 |
| Monotonic deadline and timer checks | 15 |
| Trusted checkpoint/crash protocol checks | 19 |
| Phase 3 Windows SQLite service checks | 11 |
| Real EF/in-memory policy checks, including Phase 2 and Phase 3 | 112 |
| Phase 3 headless input/command/modal checks | 19 |
| Adjacent Phase 2/security/migration/account adapter checks | 163 |
| Adjacent Phase 2 headless CUI/lifecycle checks | 13 |
| Phase 1 production gate/integrity checks | 55 |
| Direct Avalonia/VM unprovisioned-denial checks | 2 |

Reproduction commands:

```powershell
dotnet build HippoCrm.slnx --configuration Debug --no-restore -p:UseSharedCompilation=false -p:BuildInParallel=false -m:1
dotnet tests/HippoCrm.FoundationChecks/bin/Debug/net10.0/HippoCrm.FoundationChecks.dll
dotnet tests/HippoCrm.SecurityChecks/bin/Debug/net10.0/HippoCrm.SecurityChecks.dll --phase3-security
dotnet tests/HippoCrm.SecurityChecks/bin/Debug/net10.0/HippoCrm.SecurityChecks.dll --phase2-security
dotnet tests/HippoCrm.EfPolicyChecks/bin/Debug/net10.0/HippoCrm.EfPolicyChecks.dll
dotnet tests/HippoCrm.UiTests/bin/Debug/net10.0/HippoCrm.UiTests.dll --phase3-security
dotnet tests/HippoCrm.UiTests/bin/Debug/net10.0/HippoCrm.UiTests.dll --phase2-security
dotnet tests/HippoCrm.UiTests/bin/Debug/net10.0/HippoCrm.UiTests.dll --phase1-security
dotnet tests/HippoCrm.Tests/bin/Debug/net10.0/HippoCrm.Tests.dll --phase1-security
```

The checkpoint fixture models durable states and injected failures; process memory does not certify hardware rollback resistance, exclusive cross-process leases, physical power-loss durability or authenticated recovery tooling. Real EF tests perform hostile table replacement and SQLite snapshot restoration only inside disposable in-memory databases; no user workspace is opened or altered. Headless view tests attach inert, unpersisted credentials using test-only reflection and keep the production startup-denial check intact.

Native macOS suspend/resume and fsync behavior, the provisioned SQLCipher distribution, hardware/service checkpoint durability, client recovery tooling, and a physical machine with disabled network adapters still require deployment acceptance. No network adapters were changed on this host. Debug builds retain the pre-existing ImageSharp licensing warning; no licensing or Phase-4 dependency/CI policy change is included.
