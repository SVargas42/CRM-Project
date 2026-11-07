# macOS fictional Debug workspace setup

This branch uses local owner-only key files for the macOS Debug demo. Use fictional data only. This is not a production security deployment.

1. Pull the branch that contains this change (test-main) using your normal Git workflow, then check your branch and `git log -1`.
2. Quit any running CRM. **Before the first launch after this change**, rename any existing folder `~/Library/Application Support/BigEagleGroup/HippoCrm/UiDevelopment`, for example to `UiDevelopment-before-file-keys`. Keep the old folder intact. It used Keychain keys; the new path will not import them. Old Keychain items can stay and are ignored.
3. Build in **Debug** with the provisioned offline dependencies. Run with exactly one argument:

   `dotnet run --project src/HippoCrm.Desktop --configuration Debug -- --ui-development`

   Alternatively choose the IDE profile **HippoCRM UI development (Debug only)**. Do not add a seed argument or any other argument.
4. The demo creates a fresh `UiDevelopment` folder at the path above. `workspace.key` and `audit-master.key` each contain one key and are created owner-read/write only. Keep the entire folder together. Missing keys for an existing database/checkpoint cause recovery refusal; the app will not silently replace them. Do not copy individual files or delete keys to resolve an error.
5. Create the administrator account and verify the visible **UI DEVELOPMENT / LOCAL DEMO / NO OPERATIONAL DATA** marking. Use only fictional data. Normal or Release launches are expected to refuse to start on macOS under the existing production security gate.
6. If it fails, send the exact visible message, whether you used Debug and the exact argument/profile, your branch and `git log -1`. Include exception type/SQLite codes from the IDE if available, but never send passwords, key files or record text. The key-file exception is compiled out of Release; remove it before real deployment/data.

The shell path above is the expected macOS LocalApplicationData location; the program obtains it from .NET. Actual macOS path, permissions and prompt-free operation still need confirmation on a Mac.
