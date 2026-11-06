# macOS merge resolution — pending manual approval

Resolved the conflict markers in five working files on `test-main`. No Git commands, staging, commits, remote operations, package downloads, or user-workspace resets were performed. The Git index still requires resolution after approval.

## Conflicted files

| File | Resolution |
| --- | --- |
| `src/HippoCrm.Core/HippoCrm.Core.csproj` | Retained HEAD's SDK import order, Release license warning import, Release public-key resource and non-Debug exclusion of development adapters. Embedded both historical migration resources 006 and 007. |
| `src/HippoCrm.Core/CrmService.cs` | Combined macOS prefix/middle-name/suffix lookup with HEAD's parameterized lifecycle, CUI and grant predicates. Normalize CRLF/CR/LF in stored middle names for cross-platform search. Preserve first/last-name phrase searches and require a first or last name, with CUI marking validated first. |
| `src/HippoCrm.Core/SchemaManager.cs` | Reconciled the competing schema versions with additive version 8. Keep HEAD's required existing audit anchor for version 6 and later, classification migration, retention initialization and signed transactional migration auditing. Add missing name columns by inspecting their presence; never replay migration 006 over existing columns or downgrade the version. |
| `src/HippoCrm.Core/Schema.sql` | New workspaces use version 8 and include prefix, middle names and suffix alongside the CUI classification and authorization schema. |
| `tests/HippoCrm.SecurityChecks/Program.cs` | Resolved migration expectations to version 8 while preserving security checks. |

## Additional regression coverage

`tests/HippoCrm.SecurityChecks/MacosMergeChecks.cs` adds 22 checks shared by the adapter and actual EF policy suites: expanded-name lookup, newline compatibility, four lifecycle statuses, CUI/grant denial, invalid marking/name rejection, version-7 migration, rollback when audit insertion fails, preservation of existing CUI approvals, idempotent reopen, version-6 name preservation/default-denied CUI access, and rejection of a missing required audit anchor.

`tests/HippoCrm.SecurityChecks/PhaseTwoSecurityChecks.cs` invokes these checks and expects version 8 on successful migration. `tests/HippoCrm.EfPolicyChecks/HippoCrm.EfPolicyChecks.csproj` links the shared test file. `tests/HippoCrm.FoundationChecks/Program.cs` includes it in Roslyn syntax and Hungarian-name checks.

## Preservation and compatibility

Production FIPS, manifest and external trusted-checkpoint gates remain intact. The Roslyn SDK-relative reference paths remain intact. The persistent Debug workspace/checkpoint implementation remains intact and excluded from Release. Existing macOS editor/profile name fields and related model/ViewModel changes remain in the merged tree. No run or packaging script was removed; no `Run-Mac.command` or other `.command` file was present in the supplied working tree.

Version 8 intentionally supersedes both branches' competing version numbers. Older binaries that only support version 7 cannot open an upgraded workspace. Do not reset the database or OS credentials to resolve this incompatibility. An unanchored macOS version-6 database is not automatically trusted; it requires approved recovery/provisioning.

## Verification on Windows

- Full solution `dotnet build --no-restore`: Debug and Release passed with zero errors.
- Visual Studio Community MSBuild, Debug solution build: passed.
- Adapter Phase 2 and adjacent checks: 185 passed.
- Real EF/in-memory policy checks: 134 passed.
- Foundation checks: 122 passed, plus 9 memory, 15 monotonic deadline and 19 trusted-checkpoint protocol checks.
- Production Phase 1 gate checks: 55 passed in Debug and 55 in Release.
- Persistent Debug workspace checks: 29 passed, including cross-process lease exclusion and restart login/data retention.
- Release development-adapter exclusion checks: 8 passed.
- Headless session/input checks: 19 passed.
- Working source scan: no remaining merge markers.

Builds used existing restored local packages without restore or network commands. Existing ImageSharp license warnings remain under the previously approved warning policy. Native macOS execution, disabled-network-adapter testing, and client-provisioned FIPS/manifest/hardware-checkpoint acceptance were not performed on this Windows host; these results do not certify those environments.
