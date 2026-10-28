# Team delivery plan

## Implemented workflows

The application has first-run administrator creation, sign-in with persisted lockout, automatic session lock, light/dark themes, organization and contact editors, combined search filters, archive/restore, immutable authored interaction notes, employment movements, contract history, and basic dashboard metrics. SQLite retention triggers, foreign keys, exact-cent values, and atomic audit records support these workflows. The integration runner exercises core rules against actual SQLite storage.

## Team member 1 — Media and contact experience

Own desktop contact/organization presentation and media storage. Primary file: root `CRM.cs` (contact and organization editors), plus new media services/views.

1. Add contact photographs and organization logos using Avalonia's local file picker. Copy selected images to the app data directory; use managed relative identifiers rather than storing arbitrary external paths.
2. Implement a versioned, additive schema migration for media metadata. Keep original media and prior versions when replacing images; archive instead of deleting.
3. Validate image formats, size limits, and path containment. Add image previews with an accessible default avatar and safe failure handling for missing/corrupted files.
4. Add unsaved-change prompts for navigation, filter changes, session locking, and window close. Add keyboard navigation, validation focus, and explicit empty states.
5. Extract page classes as needed without changing `CrmService` signatures used by the other workstream.

Acceptance: import images fully offline; restart and see the same images; replacement retains history; invalid inputs do not crash; profile edits are protected from accidental navigation; light/dark and keyboard workflows work on both OS targets.

## Team member 2 — Security, deployment, and operations

Own packaging, administrator operations, backups, and OS verification. Primary areas: `scripts/`, release configuration, new administration/backup services, and core migration infrastructure.

1. Prepare a Windows installer and macOS application bundle. Sign/notarize with the project's real release credentials; verify clean installs on Windows and both Mac architectures.
2. Implement local backup/restore with SQLite's backup API, integrity verification, archive retention, and an explicit restore confirmation. Do not copy an open database as the backup implementation.
3. Add administrator-managed additional users, password changes/recovery, and immutable user-management audit events. No universal or embedded recovery password.
4. Resolve database encryption and local key protection requirements with the client. Verify filesystem permissions and OS lock integration. Review authentication and concurrency behavior.
5. Add release CI and migration upgrade tests. The `schema_version` table currently initializes version 1; implement a transactional forward migration runner before changing existing schema. Coordinate the media migration number with member 1.

Acceptance: a second user can sign in; backup and restore preserve notes/history/media; an existing version-1 database upgrades without data loss; installation and offline operation are verified on real target systems; sensitive-data protections are documented and checked.

## Shared integration checklist

- Build and run the core integration checks before submitting changes.
- Keep all data access in core services with parameterized queries and transactional audit entries.
- Never introduce delete buttons, cascading deletes, or replacement operations that discard records.
- Keep operating-time network calls, telemetry, call/text launching, voice tools, interactive maps, mobile support, and advanced cross-period statistics outside this scope.
- Merge additive changes in small increments; agree on schema migration ordering before editing schema.
- Test with fictional records only. Run a final offline user journey: create organization → create contact → place contact → add contract → record interaction → search → archive → restore → restart.

## Milestones

- **October 31, 2026:** MVP review with core workflows, media previews, and a reproducible Windows build.
- **November 7, 2026:** target for installation files, Mac validation, operational instructions, and final acceptance.

Confirm whether “October 31 at midnight” means the start or end of that date with the client; plan the review build before the earlier boundary until clarified.
