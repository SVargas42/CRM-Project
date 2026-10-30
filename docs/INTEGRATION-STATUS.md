# Saved-feature integration status — October 30, 2026

The primary working copy is the Visual Studio Community checkout. Integrations use small, focused commits; the recovery stash remains available and must not be applied wholesale over the current security implementation.

## Integrated

- Workflow guidance: primary checkout, safe branch tracking, and 1–5 related files per commit/push.
- Stale-edit protection: versioned contact/organization revisions, transactional schema upgrade to version 3, and rejection of saves based on older revisions. Feature commit: `997213a`.
- Account-management backend: administrator-only account listing and enable/disable, administrator password reset, current-password-verified personal password changes, session revocation, and transactional audit events. The desktop settings screen is not included in this backend batch.

The account-management integration was checked against the complete existing offline verification suite before release. Its security/migration/view-model runner contains 100 passing checks. The full run also exercises the encrypted database, existing application workflows, and headless UI; this is regression evidence, not a guarantee against every defect or a replacement for native OS acceptance.

## Saved work still requiring adaptation

| Feature | Why the older code cannot simply be copied into main |
| --- | --- |
| Photographs, organization logos, retained image versions | The saved implementation stores plaintext image files. Media must comply with encrypted storage, record-level permissions, and the current migration architecture. |
| Local backup and restore | The saved implementation exports a plaintext SQLite/media archive. The current app uses SQLCipher and OS-protected keys; encrypted exports, key recovery, designated secure destinations, retention, and restore integrity require compatible implementation and tests. |
| Unsaved-edit prompts and recovery across locking | The saved code targets the older window/controller design. It needs to fit the current MVVM navigation and must preserve immediate data obscuring and permission revalidation on lock. |
| Desktop administration/settings controls | Backend APIs now exist, but the old settings screen cannot be copied over the current shell. Add views and view-model commands without placing authentication or SQL workflows in the view. |
| Windows installer and macOS packaging/CI | Saved scripts are drafts requiring review and actual package/installation testing. macOS also requires an approved native key store. Production signing/notarization depends on release credentials. |

Existing authentication, RBAC, encryption, audit, and current UI code take precedence over older overlapping versions. Original application entry/UI files, plaintext schema/authentication replacements, and outdated dependency lock files from the stash are not candidates for wholesale restoration.

## Remaining acceptance limits

Native Windows/Mac installation, signing/notarization, keyboard/accessibility acceptance, and a test run with network adapters physically disabled remain separate release work. Headless tests and a local-only package restore do not establish those results.

Use fictional records for development checks. Keep production databases, credentials, keys, generated binaries, and recovery snapshots out of Git.
