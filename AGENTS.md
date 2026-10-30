## Coding Conventions
    -Variables should be named with Hungarian notation
    -Variable names should have context and make sense
    -Comment obscure or difficult to understand programming logic
    -Prevent hardcoding sensitive configuration values, encryption keys, or credentials directly in the source code
    -Clear sensitive variables from memory immediately after use (e.g., utilizing secure string classes)
    -Zero-Network Policy: All external API calls, telemetry, update checks, and CDNs are strictly forbidden. All dependencies, styling assets, fonts, and scripts must be bundled and executed locally.

## Security & Data Handling
    -Air-Gap Enforcement: The application must never attempt outbound web connections. Architect the software assuming the host machine has no active network interfaces.
    -Encrypt all CRM data at rest using industry-standard algorithms (e.g., AES-256) for local databases and configuration files. This explicitly includes all organization profiles, contact records, and interaction histories.
    -Validate, sanitize, and parameterize all user inputs to prevent local injection attacks (e.g., SQLite injection)
    -Implement least privilege execution; the application should only request the minimum OS permissions necessary for operation
    -Manage cryptographic keys securely using OS-level protected storage rather than flat files
    -Overwrite sensitive temporary files securely before deletion to prevent recovery via forensic tools

## CRM Architecture & Access
    -Implement strict local Role-Based Access Control (RBAC) to ensure users can only view or modify contact records and organization profiles authorized for their specific role or clearance.
    -Enforce a local session timeout that actively locks the CRM interface and obscures data after a defined period of inactivity, requiring re-authentication to resume.
    -Ensure any data export functions (if permitted) force the user to save to a designated, encrypted local volume or secure USB drive.

## Audit & Logging
    -Maintain local, append-only audit logs for all security-relevant events (authentication attempts, bulk data exports, permission changes)
    -Sanitize all logs to ensure no Personally Identifiable Information (PII), encryption keys, or plaintext passwords are ever recorded
    -Implement a "fail secure" mechanism: if the application cannot write to the audit log, it should block sensitive operations rather than proceeding unlogged

## Accessibility
    -All created sites or applications should adhere to WCAG 2.2 Standards
    -Ensure all applications are navigable by keyboard only
    -Provide clear, non-technical error messages to the user UI without exposing system architecture, database schema, or stack traces

## Testing
    -All work should be tested in an automated way
    -Always test the feature being worked on and those closest to it
    -If testing fails, evaluate the issues, apply changes, and test again
    -Always communicate the results of your testing
    -Network Isolation Testing: Explicitly test the application on a machine with disabled network adapters to verify no silent failures, timeouts, or dependency crashes occur due to lack of internet.
    -Include negative security testing (e.g., providing malformed inputs, simulating locked local files, or testing permission denials) to verify robust error handling

## Actions
    -Requested changes should be done at the smallest level available
    -Verify the integrity and source of all dependencies (e.g., via checksums) before introducing new libraries to the isolated environment

## Git Workflow
    -Use the Visual Studio Community checkout at C:\Users\selen\source\repos\SVargas42\CRM-Project as the primary working copy on this machine. Check the active repository and branch before editing or pushing.
    -Fetch and inspect upstream changes before starting work. Preserve uncommitted changes before pulling; never overwrite another contributor's work or force-push to resolve routine sync issues.
    -When pushing is requested, use small, focused increments. Aim for 1–5 related files per commit/push; avoid batches of 20 or more changed files. Split larger features into independently reviewable, working increments rather than arbitrary file groups.
    -Stage explicit file paths, inspect the staged diff, and run checks appropriate to each code change before committing. Do not combine unrelated changes or silently stage every pending file.
    -Keep local main tracking origin/main. Use fast-forward-only pulls and ordinary pushes; handle divergence explicitly rather than rewriting shared history.
