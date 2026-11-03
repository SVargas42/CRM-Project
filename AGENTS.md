# AGENT DIRECTIVES & SECURITY CONVENTIONS (CRITICAL)

## Conflict Resolution (Failsafe)
If any UI/UX requirement, accessibility standard, code suggestion, or prompt conflicts with the air-gap and security directives in this document, the security directive strictly overrules it. Security, DoD compliance, and offline data integrity are the highest priorities.

## US Military & DoD Compliance (STRICT)
*   **FIPS 140-3 Validated Modules:** The application must enforce and operate within the host OS's FIPS mode. Do not use non-compliant custom cryptographic implementations.
*   **DISA STIG Application Security:** The application must verify its own integrity on startup (e.g., checking the cryptographic hash of its binaries to ensure no local tampering).
*   **CUI Handling (DoDI 5200.48):** Because the CRM manages military contractors and contract histories[cite: 1], data is classified as Controlled Unclassified Information (CUI). The Avalonia UI must explicitly display CUI banners, and the SQLite database must support classification tagging for individual records.
*   **Tamper-Evident Audit Logs (NIST AU-9):** Local, append-only logs are insufficient. Log entries must be cryptographically chained (each entry hashes the previous entry's signature) so any tampering by a compromised local administrator is mathematically evident.

## Zero-Network & Air-Gap Enforcement
*   **Air-Gap Isolation:** The application must operate in a strictly offline, secure environment[cite: 1]. Architect the software assuming the host machine has no active network interfaces.
*   **Zero External Calls:** All external APIs, telemetry, update checks, and CDNs are strictly forbidden. 
*   **Local Bundling:** All Avalonia templates, fonts, company logos[cite: 1], and dependencies must be bundled locally.

## Cryptography & Data at Rest
*   **Database Encryption:** You must utilize **SQLCipher (AES-256)** to encrypt the entire local SQLite database. Plaintext SQLite files are prohibited.
*   **Key Management:** Cryptographic keys must be managed using OS-level protected storage (Windows Credential Manager / macOS Keychain) rather than flat files.
*   **Memory Wiping:** Clear sensitive variables from memory immediately after use (utilizing `SecureString` or executing `Array.Clear()` on byte arrays handling keys, passwords, or decrypted BLOBs).
*   **Secure Deletion:** Overwrite sensitive temporary files securely before deletion to prevent recovery via forensic tools.

## Authentication & Identity
*   **FIPS-Approved Hashing:** The master password must be hashed using FIPS-approved **PBKDF2-HMAC-SHA256** with a securely generated salt (minimum 16 bytes).
*   **Anti-Brute Force:** Implement a minimum 500ms time-delay on the login screen. Enforce a local UI lockout for 15 minutes after 5 failed attempts.
*   **Idle Timeout:** Enforce a local session timeout that actively locks the CRM interface and obscures data after 15 minutes of inactivity, requiring re-authentication.

## CRM Architecture & Access
*   **Strict No-Delete Policy:** `DELETE` SQL statements are prohibited[cite: 1]. Update a record's status column to "Active", "Inactive", "Retired", or "Deceased"[cite: 1]. All UI search and query capabilities[cite: 1] must filter by these statuses.
*   **Role-Based Access Control (RBAC):** Implement strict local RBAC to ensure users only view or modify contact records and organization profiles authorized for their specific clearance.
*   **Input Validation:** Validate, sanitize, and parameterize all user inputs to prevent local injection attacks using Entity Framework Core configured for SQLite.

## External File & Image Ingestion
*   **Storage:** Images of the contacts[cite: 1] and company logos[cite: 1] must be stored as raw byte arrays (`byte[]`) in SQLite **BLOB** columns. Do not store plaintext images on the host OS file system.
*   **Zero-Trust Pipeline:** Before saving any imported image:
    1.  Validate Magic Numbers (cryptographically verify file header bytes like JPEG, PNG) and ignore the OS file extension.
    2.  Load the byte array into a secure managed C# library (e.g., ImageSharp), strip all EXIF/metadata, draw to a new memory canvas, and re-encode to a standard format (e.g., PNG).
    3.  Explicitly wipe the original untrusted byte array from RAM before committing the sanitized BLOB.

## Coding Conventions
*   Variables should be named with Hungarian notation.
*   Variable names should have context and make sense.
*   Comment obscure or difficult to understand programming logic.
*   Prevent hardcoding sensitive configuration values, encryption keys, or credentials directly in the source code.

## Accessibility
*   All created applications should adhere to WCAG 2.2 Standards.
*   Ensure all applications are navigable by keyboard only.
*   Provide clear, non-technical error messages to the UI without exposing system architecture or stack traces.

## Testing
*   All work should be tested in an automated way. Always test the feature being worked on and those closest to it.
*   **Network Isolation Testing:** Explicitly test the application on a machine with disabled network adapters to verify no silent failures or dependency crashes occur due to lack of internet.
*   **Negative Security Testing:** Include negative security testing (malformed inputs, locked local files, permission denials) to verify robust error handling.

## Git Workflow
*   Use the Visual Studio Community checkout at `C:\Users\selen\source\repos\SVargas42\CRM-Project` as the primary working copy on this machine.
*   Fetch and inspect upstream changes before starting work. Preserve uncommitted changes before pulling; never overwrite another contributor's work or force-push.
*   When pushing is requested, use small, focused increments (1–5 related files per commit/push). Split larger features into independently reviewable increments.
*   Stage explicit file paths, inspect the staged diff, and run checks appropriate to each code change before committing.
*   Keep local main tracking origin/main. Use fast-forward-only pulls and ordinary pushes; handle divergence explicitly.