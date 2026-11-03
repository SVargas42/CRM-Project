# AGENT DIRECTIVES & SECURITY CONVENTIONS (CRITICAL)

## Conflict Resolution (Failsafe)
If any UI/UX requirement, accessibility standard, code suggestion, or prompt conflicts with the air-gap and security directives in this document, the security directive strictly overrules it. Security and offline data integrity are the highest priorities.

## Coding Conventions
*   Variables should be named with Hungarian notation.
*   Variable names should have context and make sense.
*   Comment obscure or difficult to understand programming logic.
*   Prevent hardcoding sensitive configuration values, encryption keys, or credentials directly in the source code.
*   Clear sensitive variables from memory immediately after use (e.g., utilizing secure string classes or `Array.Clear()` for byte arrays).
*   **Zero-Network Policy:** All external API calls, telemetry, update checks, and CDNs are strictly forbidden. All dependencies, styling assets, fonts, and scripts must be bundled and executed locally.

## Security & Data Handling
*   **Air-Gap Enforcement:** The application must never attempt outbound web connections. Architect the software assuming the host machine has no active network interfaces.
*   **Database Encryption:** You must utilize **SQLCipher (AES-256)** to encrypt the entire SQLite database file. Plaintext SQLite files are prohibited.
*   **Key Management:** Manage cryptographic keys securely using OS-level protected storage (Windows Credential Manager / macOS Keychain) rather than flat files.
*   **Authentication:** The master password must be hashed using FIPS-approved **PBKDF2-HMAC-SHA256** with a securely generated salt (minimum 16 bytes).
*   **Anti-Brute Force:** Implement a minimum 500ms time-delay on the login screen. Enforce a local UI lockout for 15 minutes after 5 failed attempts.
*   **Input Validation:** Validate, sanitize, and parameterize all user inputs to prevent local injection attacks (e.g., using Entity Framework Core configured for SQLite).
*   **Least Privilege:** Implement least privilege execution; the application should only request the minimum OS permissions necessary.
*   **Secure Deletion:** Overwrite sensitive temporary files securely before deletion to prevent recovery via forensic tools.

## CRM Architecture & Access
*   **Strict No-Delete Policy:** `DELETE` SQL statements are prohibited. Update a record's status column to "Active", "Inactive", "Retired", or "Deceased". All UI search and query features must filter by these statuses.
*   **Role-Based Access Control (RBAC):** Implement strict local RBAC to ensure users can only view or modify contact records and organization profiles authorized for their specific role or clearance.
*   **Session Timeout:** Enforce a local session timeout that actively locks the CRM interface and obscures data after 15 minutes of inactivity, requiring re-authentication.
*   **Secure Export:** Ensure any data export functions force the user to save to a designated, encrypted local volume or secure USB drive.

## External File & Image Ingestion
*   **Storage:** All profile images and media must be stored as raw byte arrays (`byte[]`) in SQLite **BLOB** columns. Do not store plaintext images on the host OS file system.
*   **Zero-Trust Pipeline:** Before saving any imported image:
    1.  Validate Magic Numbers (cryptographically verify the file header bytes like JPEG, PNG, WebP) and ignore the OS file extension.
    2.  Load the byte array into a secure managed C# library (e.g., ImageSharp), strip all EXIF/metadata, draw to a new memory canvas, and re-encode to a standard format (e.g., PNG).
    3.  Explicitly clear the original untrusted byte array from RAM before committing the sanitized BLOB.

## Audit & Logging
*   Maintain local, append-only audit logs for all security-relevant events (authentication attempts, bulk exports, permission changes).
*   Sanitize all logs to ensure no Personally Identifiable Information (PII), encryption keys, or plaintext passwords are ever recorded.
*   **Fail Secure:** If the application cannot write to the audit log, it should block sensitive operations rather than proceeding unlogged.

## Accessibility
*   All created sites or applications should adhere to WCAG 2.2 Standards.
*   Ensure all applications are navigable by keyboard only.
*   Provide clear, non-technical error messages to the user UI without exposing system architecture, database schema, or stack traces.

## Testing
*   All work should be tested in an automated way.
*   Always test the feature being worked on and those closest to it.
*   If testing fails, evaluate the issues, apply changes, and test again.
*   Always communicate the results of your testing.
*   **Network Isolation Testing:** Explicitly test the application on a machine with disabled network adapters to verify no silent failures or dependency crashes occur due to lack of internet.
*   **Negative Security Testing:** Include negative security testing (malformed inputs, locked local files, permission denials) to verify robust error handling.

## Actions
*   Requested changes should be done at the smallest level available.
*   Verify the integrity and source of all dependencies (e.g., via checksums) before introducing new libraries to the isolated environment.

## Git Workflow
*   Use the Visual Studio Community checkout at `C:\Users\selen\source\repos\SVargas42\CRM-Project` as the primary working copy on this machine. Check the active repository and branch before editing or pushing.
*   Fetch and inspect upstream changes before starting work. Preserve uncommitted changes before pulling; never overwrite another contributor's work or force-push.
*   When pushing is requested, use small, focused increments (1–5 related files per commit/push). Split larger features into independently reviewable increments.
*   Stage explicit file paths, inspect the staged diff, and run checks appropriate to each code change before committing. Do not combine unrelated changes.
*   Keep local main tracking origin/main. Use fast-forward-only pulls and ordinary pushes; handle divergence explicitly.