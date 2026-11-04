# Pre-commit security review and proposed commit plan

Review date: 2026-11-03. Checkout: test1-branch. No staging, commits, pushes, remote fetches or normal-workspace database access were performed.

## Scope and inventory

The initial pending set contained 7 modified tracked files and 46 untracked .nupkg archives (53 files). All initial files were reviewed. Because HippoCrm.Core.csproj re-enables previously excluded code, the review also covered CrmDbContext.cs, ImageSanitizer.cs and ProfileImageService.cs and their existing authorization/connection boundaries. The final proposed set contains 61 files: the original 53 plus 3 hardened source files, a test entry-point change, a focused test file, a package inventory, a feed README and this report.

This is a review of pending changes and the reactivated code paths, not a complete source audit or certification of vendor binaries or the entire CRM.

## Findings and fixes

- HIGH: CrmDbContext exposed direct EF query/bulk-SQL execution without the service RBAC/audit boundary. Added scoped authorized contact and organization read gateways, live session validation, record grants, existing status/archive filters and same-transaction auditing. A command interceptor blocks synchronous/asynchronous EF commands outside the authorized gateway. Deletion remains prohibited; lifecycle status changes continue through retained-status operations.
- HIGH: An EF context could retain a prior actor's tracked entities for Local/Find access without a database command. Successful authorized writes now clear tracking. Modified records cannot overwrite their original creation timestamp.
- MEDIUM: An opened SQLCipher connection could outlive an EF context that was disposed before model initialization. Synchronous/asynchronous disposal now explicitly closes the owned connection, including unused contexts.
- HIGH: The image encoder used an expanding MemoryStream that could abandon earlier plaintext buffers, and image allocator buffers were not operation-owned/pinned. The pipeline now uses a fixed pinned output array and an operation-owned pinned allocator that clears all retained buffers on normal or exceptional completion. Per-image allocation and dimension/input limits remain enforced.
- MEDIUM: The image service pinned the original input only after authorization/database work. It now pins on entry and clears/releases both original and sanitized buffers on every exit, including authorization denial or audit failure.
- MEDIUM: A local package source alone did not require signed new packages. NuGet.Config now requires signatures in addition to clearing inherited external/fallback/audit sources. Offline signature checks were run explicitly with NUGET_CERT_REVOCATION_MODE=offline.

No new production plaintext password/key handling, raw DELETE/EF Remove calls, unkeyed SQLite connection paths, external URI execution, remote UI assets or hardcoded Windows runtime paths were found in the reviewed changes. New raw commands in the negative tests deliberately attempt forbidden operations and are asserted to fail before execution. Existing DELETE-blocking triggers and CLR collection Clear operations are not record-deletion endpoints.

## Original tracked-file disposition

| File | Review result |
| --- | --- |
| MacPortInfo.txt | Documentation updates, native/macOS limitations and licensing caveats; no executable paths introduced. |
| NuGet.Config | Relative LocalPackages source only; inherited sources/fallback/audit sources cleared; signed packages now required. |
| src/HippoCrm.Core/HippoCrm.Core.csproj | Exact-version EF SQLite .Core and ImageSharp references; SQLCipher bundle preserved; reactivated classes hardened. |
| src/HippoCrm.Core/packages.lock.json | Resolved identities, versions and content hashes match archived packages; no bundle_e_sqlite3. |
| src/HippoCrm.Desktop/packages.lock.json | Same checks; app graph remains on SQLCipher and packaged platform assets. |
| tests/HippoCrm.Tests/packages.lock.json | Same checks for the Core integration test graph. |
| tests/HippoCrm.UiTests/packages.lock.json | Same checks for the desktop/headless test graph. |

## Package checks and limits

All 46 archives have matching identities, versions and NuGet content hashes across all four changed lock files. All 46 passed dotnet nuget verify --all with certificate revocation explicitly offline. No unsafe absolute/path-traversal archive entries were found. Manifest/build-asset inventories were reviewed; the ImageSharp license task has no direct System.Net or Process type references in its managed metadata. Avalonia.BuildServices targets are not imported by the app/headless test generated NuGet targets, and Remote.Protocol runtime assets remain excluded. URLs in package descriptions, certificates, licenses and source metadata are not application network calls.

package-inventory.json records local verification facts, not independent approval. Full reverse engineering of vendor managed/native binaries was not performed. An offline signature pass does not establish current revocation status, absence of all vulnerabilities, organizational approval, licensing suitability or FIPS validation.

Package archives total 196,212,879 bytes. The largest archive is 71,804,127 bytes. Approve the repository's binary-storage/distribution approach before staging these batches.

## Remaining release gates

- FIPS validation and host FIPS operation remain unestablished. The community SQLCipher bundle/version probe and .NET PBKDF2 API names are not proof of a validated deployed module. An approved native distribution/module certificate and deployment evidence are required before representing the application as DoD/FIPS compliant.
- The supplied ImageSharp package reports a missing license. Debug compilation succeeds with that warning; its build target requires a valid license for Release. No license key, bypass or approval was fabricated.
- Current certificate revocation status could not be determined from cached revocation data. Independent dependency approval and vulnerability review remain required.
- Native macOS execution and a machine-level test with disabled network adapters were not performed in this review. The changed runtime code uses managed cross-platform APIs and the existing OS-key-store/SQLCipher factory.
- Previously observed unrelated UI smoke-test sign-in-button sizing failure is outside this change set and was not fixed or waived here.

## Validation

- Full Windows Debug solution build: zero errors; existing ImageSharp licensing warning.
- Offline locked restore after signature enforcement: all six projects passed.
- 46 focused pre-commit security checks passed, including direct/async EF read denial, record grants, revocation, audit-failure rollback, tracked/bulk/raw deletion denial, unused-context disposal, cross-session tracking cleanup with generated-ID preservation, PNG/JPEG validation, metadata stripping, malformed/oversized input, decoder buffer disposal, image RBAC and retained BLOB revisions.
- Foundation checks: 62 passed, plus 9 password/session memory checks.
- All 46 NuGet signature verifications passed offline; every changed lock entry matches the local archive inventory.
- git diff --check passed. The Git index remains unchanged.

## Proposed ordered commit batches

These are engineering review batches, not permission to release. Do not stage or commit until the user approves this plan. Package batches come first, then source/configuration changes; this keeps the offline feed available before dependency reactivation. The security source fixes precede reactivation. Each batch contains at most five related files. Do not include artifacts/, logs, test databases, credentials, private license keys or machine-specific files.

### Batch 1: chore(deps): vendor pinned SQLCipher packages

- LocalPackages/microsoft.data.sqlite.core.10.0.14.nupkg
- LocalPackages/sqlitepclraw.bundle_e_sqlcipher.2.1.11.nupkg
- LocalPackages/sqlitepclraw.core.2.1.13.nupkg
- LocalPackages/sqlitepclraw.lib.e_sqlcipher.2.1.11.nupkg
- LocalPackages/sqlitepclraw.provider.e_sqlcipher.2.1.11.nupkg

### Batch 2: chore(deps): vendor EF Core SQLite provider packages

- LocalPackages/microsoft.entityframeworkcore.10.0.14.nupkg
- LocalPackages/microsoft.entityframeworkcore.abstractions.10.0.14.nupkg
- LocalPackages/microsoft.entityframeworkcore.analyzers.10.0.14.nupkg
- LocalPackages/microsoft.entityframeworkcore.relational.10.0.14.nupkg
- LocalPackages/microsoft.entityframeworkcore.sqlite.core.10.0.14.nupkg

### Batch 3: chore(deps): vendor caching and configuration packages

- LocalPackages/microsoft.extensions.caching.abstractions.10.0.14.nupkg
- LocalPackages/microsoft.extensions.caching.memory.10.0.14.nupkg
- LocalPackages/microsoft.extensions.configuration.abstractions.10.0.14.nupkg
- LocalPackages/microsoft.extensions.options.10.0.14.nupkg
- LocalPackages/microsoft.extensions.primitives.10.0.14.nupkg

### Batch 4: chore(deps): vendor dependency injection and logging packages

- LocalPackages/microsoft.extensions.dependencyinjection.10.0.14.nupkg
- LocalPackages/microsoft.extensions.dependencyinjection.abstractions.10.0.14.nupkg
- LocalPackages/microsoft.extensions.dependencymodel.10.0.14.nupkg
- LocalPackages/microsoft.extensions.logging.10.0.14.nupkg
- LocalPackages/microsoft.extensions.logging.abstractions.10.0.14.nupkg

### Batch 5: chore(deps): vendor Avalonia core theme and test packages

- LocalPackages/avalonia.11.3.22.nupkg
- LocalPackages/avalonia.desktop.11.3.22.nupkg
- LocalPackages/avalonia.fonts.inter.11.3.22.nupkg
- LocalPackages/avalonia.themes.fluent.11.3.22.nupkg
- LocalPackages/avalonia.headless.11.3.22.nupkg

### Batch 6: chore(deps): vendor Avalonia platform backends

- LocalPackages/avalonia.native.11.3.22.nupkg
- LocalPackages/avalonia.win32.11.3.22.nupkg
- LocalPackages/avalonia.x11.11.3.22.nupkg
- LocalPackages/avalonia.freedesktop.11.3.22.nupkg
- LocalPackages/avalonia.angle.windows.natives.2.1.25547.20250602.nupkg

### Batch 7: chore(deps): vendor Avalonia rendering and support packages

- LocalPackages/avalonia.skia.11.3.22.nupkg
- LocalPackages/avalonia.buildservices.11.3.2.nupkg
- LocalPackages/avalonia.remote.protocol.11.3.22.nupkg
- LocalPackages/microcom.runtime.0.11.0.nupkg
- LocalPackages/tmds.dbus.protocol.0.21.3.nupkg

### Batch 8: chore(deps): vendor SkiaSharp runtime assets

- LocalPackages/skiasharp.2.88.9.nupkg
- LocalPackages/skiasharp.nativeassets.linux.2.88.9.nupkg
- LocalPackages/skiasharp.nativeassets.macos.2.88.9.nupkg
- LocalPackages/skiasharp.nativeassets.webassembly.2.88.9.nupkg
- LocalPackages/skiasharp.nativeassets.win32.2.88.9.nupkg

### Batch 9: chore(deps): vendor HarfBuzz runtime assets

- LocalPackages/harfbuzzsharp.8.3.1.1.nupkg
- LocalPackages/harfbuzzsharp.nativeassets.linux.8.3.1.1.nupkg
- LocalPackages/harfbuzzsharp.nativeassets.macos.8.3.1.1.nupkg
- LocalPackages/harfbuzzsharp.nativeassets.webassembly.8.3.1.1.nupkg
- LocalPackages/harfbuzzsharp.nativeassets.win32.8.3.1.1.nupkg

### Batch 10: chore(deps): vendor ImageSharp image processing package

- LocalPackages/sixlabors.imagesharp.3.2.0.nupkg

### Batch 11: build: enforce the signed offline package feed

- NuGet.Config
- LocalPackages/package-inventory.json
- LocalPackages/README.md

### Batch 12: fix(security): guard EF access and clear image buffers

- src/HippoCrm.Core/CrmDbContext.cs
- src/HippoCrm.Core/ImageSanitizer.cs
- src/HippoCrm.Core/ProfileImageService.cs

### Batch 13: build: reactivate EF and image security modules with locked dependencies

- src/HippoCrm.Core/HippoCrm.Core.csproj
- src/HippoCrm.Core/packages.lock.json
- src/HippoCrm.Desktop/packages.lock.json
- tests/HippoCrm.Tests/packages.lock.json
- tests/HippoCrm.UiTests/packages.lock.json

### Batch 14: test: cover EF authorization and secure image ingestion

- tests/HippoCrm.Tests/PreCommitSecurityChecks.cs
- tests/HippoCrm.Tests/Program.cs

### Batch 15: docs: record pre-commit security review and macOS release gates

- MacPortInfo.txt
- docs/PRE_COMMIT_SECURITY_REVIEW.md

## Complete package inventory

| Archive | Version | Bytes |
| --- | --- | ---: |
| avalonia.11.3.22.nupkg | 11.3.22 | 12988033 |
| avalonia.angle.windows.natives.2.1.25547.20250602.nupkg | 2.1.25547.20250602 | 6132962 |
| avalonia.buildservices.11.3.2.nupkg | 11.3.2 | 68329 |
| avalonia.desktop.11.3.22.nupkg | 11.3.22 | 43192 |
| avalonia.fonts.inter.11.3.22.nupkg | 11.3.22 | 2873327 |
| avalonia.freedesktop.11.3.22.nupkg | 11.3.22 | 257754 |
| avalonia.headless.11.3.22.nupkg | 11.3.22 | 121636 |
| avalonia.native.11.3.22.nupkg | 11.3.22 | 779116 |
| avalonia.remote.protocol.11.3.22.nupkg | 11.3.22 | 142564 |
| avalonia.skia.11.3.22.nupkg | 11.3.22 | 233247 |
| avalonia.themes.fluent.11.3.22.nupkg | 11.3.22 | 326414 |
| avalonia.win32.11.3.22.nupkg | 11.3.22 | 1069585 |
| avalonia.x11.11.3.22.nupkg | 11.3.22 | 501673 |
| harfbuzzsharp.8.3.1.1.nupkg | 8.3.1.1 | 1029897 |
| harfbuzzsharp.nativeassets.linux.8.3.1.1.nupkg | 8.3.1.1 | 10890070 |
| harfbuzzsharp.nativeassets.macos.8.3.1.1.nupkg | 8.3.1.1 | 1358176 |
| harfbuzzsharp.nativeassets.webassembly.8.3.1.1.nupkg | 8.3.1.1 | 13701263 |
| harfbuzzsharp.nativeassets.win32.8.3.1.1.nupkg | 8.3.1.1 | 2502549 |
| microcom.runtime.0.11.0.nupkg | 0.11.0 | 28122 |
| microsoft.data.sqlite.core.10.0.14.nupkg | 10.0.14 | 208438 |
| microsoft.entityframeworkcore.10.0.14.nupkg | 10.0.14 | 1427760 |
| microsoft.entityframeworkcore.abstractions.10.0.14.nupkg | 10.0.14 | 54016 |
| microsoft.entityframeworkcore.analyzers.10.0.14.nupkg | 10.0.14 | 52005 |
| microsoft.entityframeworkcore.relational.10.0.14.nupkg | 10.0.14 | 1043727 |
| microsoft.entityframeworkcore.sqlite.core.10.0.14.nupkg | 10.0.14 | 185890 |
| microsoft.extensions.caching.abstractions.10.0.14.nupkg | 10.0.14 | 178899 |
| microsoft.extensions.caching.memory.10.0.14.nupkg | 10.0.14 | 195144 |
| microsoft.extensions.configuration.abstractions.10.0.14.nupkg | 10.0.14 | 145194 |
| microsoft.extensions.dependencyinjection.10.0.14.nupkg | 10.0.14 | 349588 |
| microsoft.extensions.dependencyinjection.abstractions.10.0.14.nupkg | 10.0.14 | 283983 |
| microsoft.extensions.dependencymodel.10.0.14.nupkg | 10.0.14 | 269141 |
| microsoft.extensions.logging.10.0.14.nupkg | 10.0.14 | 228255 |
| microsoft.extensions.logging.abstractions.10.0.14.nupkg | 10.0.14 | 808396 |
| microsoft.extensions.options.10.0.14.nupkg | 10.0.14 | 491778 |
| microsoft.extensions.primitives.10.0.14.nupkg | 10.0.14 | 207072 |
| sixlabors.imagesharp.3.2.0.nupkg | 3.2.0 | 1096930 |
| skiasharp.2.88.9.nupkg | 2.88.9 | 7150003 |
| skiasharp.nativeassets.linux.2.88.9.nupkg | 2.88.9 | 15619648 |
| skiasharp.nativeassets.macos.2.88.9.nupkg | 2.88.9 | 6830677 |
| skiasharp.nativeassets.webassembly.2.88.9.nupkg | 2.88.9 | 71804127 |
| skiasharp.nativeassets.win32.2.88.9.nupkg | 2.88.9 | 10918525 |
| sqlitepclraw.bundle_e_sqlcipher.2.1.11.nupkg | 2.1.11 | 36767 |
| sqlitepclraw.core.2.1.13.nupkg | 2.1.13 | 36010 |
| sqlitepclraw.lib.e_sqlcipher.2.1.11.nupkg | 2.1.11 | 21094009 |
| sqlitepclraw.provider.e_sqlcipher.2.1.11.nupkg | 2.1.11 | 61200 |
| tmds.dbus.protocol.0.21.3.nupkg | 0.21.3 | 387788 |
