# Profile pictures and possible duplicates

Contacts and organizations can have a local PNG/JPEG picture after the record is saved. Choose photo/logo in the editor. Pictures save immediately without submitting unfinished profile fields. Archived records must be restored before replacing their picture. Replacement retains the prior version. The unchanged normalized picture does not create another version.

Only permitted local files under 10 MiB are accepted; URLs, UNC/mapped network storage, detected network mounts, symlinks/reparse points and cloud-storage placeholders/folders are rejected. Copy a downloaded image into a fully local folder first. Header/decode validation is independent of the filename extension. Sources are bounded to 4096 pixels per side/16 megapixels, decoded with bounded owned buffers, re-encoded without imported metadata and resized to at most 1024 pixels on the longest side. Normalized output is capped at 5 MiB. No plaintext picture copies or filename/EXIF values are stored in diagnostic logs. Source files are not deleted or modified.

Production BLOBs use the existing SQLCipher/OS key-store and approved-runtime gates. Reads, unchanged uploads and changed versions use the existing transactional audit/checkpoint protocol. Events refer to the exact image version; its immutable owner identifies contact versus company. Retained pixels, owner, author and timestamp cannot be updated through SQL. No image-delete operation is introduced. Preview-owned CPU buffers are overwritten/disposed, and cached BLOBs are cleared when leaving a record or locking. This does not claim control over OS screenshots, paging, or graphics-driver copies; operational disk/memory protections remain part of deployment.

## Possible duplicate review

A background worker checks authorized records during save, using normalized email/phone overlap or a corroborating name and address. Shared mailboxes, switchboards and family numbers are possible matches, not proof of identity. A name or picture alone never causes automatic merging. Contacts are compared to contacts; companies to companies.

- **Keep editing / Escape:** return to the unchanged draft without saving.
- **Create Separate Record** (new) / **Save Without Linking** (edit): explicitly ignore this duplicate warning and save normally. All other validation and permissions still apply. The decision is audited.
- **Save & Link for Review:** choose a writable match, save, and add a review reference. It does **not** combine or copy names, notes, contracts, employment, photographs, grants or other fields. Both original IDs and histories remain separate. Profile editors explain this beside their review references; saved-state feedback confirms it.

Cmd+Enter on macOS or Ctrl+Enter on Windows confirms saving without a link; bare Enter does not force an override. The owned, keyboard-accessible sheet follows the current theme, with restrained translucency/shadow, typography hierarchy, rounded corners and a short cubic-bezier fade. It closes when the session locks. Blur falls back to the platform's supported transparency behavior. Native picker and visual behavior still require acceptance on each target OS.

Candidate results never expose inaccessible records. Save rechecks candidate IDs/revisions, link permissions, CUI authorization, source revision and session validity in its transaction. New or changed matches require fresh review. Link decisions and saves roll back together if auditing fails. Links are immutable, idempotent retained references; they never union permissions or silently archive/delete a source record. Actual record consolidation remains a future, separately reviewed workflow.

## Storage compatibility

Audited schema version 8 adds image integrity guards and retained record links. Existing version-7 image data stays in place. The older local contact_id/organization_id/image_data table is transactionally renamed to a protected retained legacy table and copied into the current image version format, preserving IDs, authors, timestamps and pixels. Invalid owners abort the upgrade; there is no silent skipping. Original legacy BLOBs are retained. Back up only via the established encrypted workspace/key/checkpoint recovery process; do not copy old source code or plaintext databases over the current workspace.

## Verification

Restore dependencies only from the signed, verified local package feed. Run `./scripts/VerifyPhotoDuplicates.ps1` to build Debug and execute synthetic memory-only backend/UI checks. The dedicated feature modes are `--photo-duplicates` (Core) and `--photo-duplicates-ui` (UI). They do not access the normal CRM workspace. Fixture pictures/screenshots stay under ignored artifacts.

The tests cover decoding/metadata removal, buffers, size/preview bounds, retained versions, orphan/overwrite/delete denial, authorization, audit rollback, cancellation, real SQLite-full via a memory database page quota, compatibility upgrade/reopen, candidate revocation/staleness, explicit ignore/link, modal keyboard behavior and locking. Existing phase-2/3, EF, startup and fixture isolation suites remain applicable.

Production startup/security checks are unchanged. Debug demo storage is not an operational CUI deployment. Physical disabled-network tests, native file-picker closure behavior, macOS visual/keyboard acceptance and provisioned production-runtime validation remain separate acceptance checks. No new production dependency was introduced; the linked-source security runner now references the same pinned local ImageSharp package already used by Core. Its vendor licensing warning/provisioning policy is unchanged.
