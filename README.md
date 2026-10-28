# Swollen Hippo CRM

An offline desktop CRM for Swollen Hippo Contractors, developed by Big Eagle Group. C# / .NET 10, Avalonia, and SQLite. Supported release targets are Windows and macOS.

## Run

Install the .NET 10 SDK, then from this directory:

```powershell
dotnet restore HippoCrm.slnx
dotnet build HippoCrm.slnx
dotnet run --project src/HippoCrm.Desktop
```

For this workspace, an SDK was installed under `.tools/dotnet`. If `dotnet` is unavailable, substitute `./.tools/dotnet/dotnet.exe` in these commands. Development package restore requires internet; the installed application's normal operation does not. No API keys, server, browser, hosted database, or cloud account are required.

## First launch

Create an administrator username and password on the first screen. Passwords must contain 12–256 characters. There is no shared or preconfigured password. Subsequent launches require sign-in. Five incorrect passwords lock that account for 15 minutes, including across application restarts. The workspace locks after 15 minutes without keyboard or pointer input.

SQLite data is stored under `Environment.SpecialFolder.LocalApplicationData/BigEagleGroup/HippoCrm/crm.db` for the current OS user. On Windows this is normally `%LOCALAPPDATA%\BigEagleGroup\HippoCrm\crm.db`. On macOS resolve `LocalApplicationData` for the running user's environment; the application uses .NET's platform mapping. Do not put the live database in a network share or synchronized folder. Exit the application before copying the data directory for a local backup. Keep any SQLite sidecar files with the database if present.

## Workflows

- **Organizations:** create and edit contact details, locations, services, tags, and lifecycle status. Archive and restore records through the editor and archive filter.
- **Contacts:** create and edit phone numbers, emails, addresses, skills, availability, personal relationships, social profiles, and lifecycle status. Multiple phone numbers, emails, and addresses can be entered on separate lines. Apply name/email/phone, skill, and availability filters together.
- **Interactions:** save timestamped notes with the signed-in author's name. Existing notes remain immutable; corrections are additional notes.
- **Employment:** record an organization, role, and start/end dates. End a current placement before adding another, preserving movement history. Dates use `YYYY-MM-DD`; placement intervals are inclusive and cannot overlap.
- **Contracts:** append dated values, locations, and descriptions. Values use exact integer cents in storage; agree on one currency for this deployment. Most recent contracts appear first.
- **Overview:** view record counts, active and available contacts, contacts needing follow-up, and four weeks of interaction activity. Follow-up means an active contact with no note in 30 days. Counts update when reopening Overview.
- **Appearance:** switch between light and dark modes using the sidebar.

Save profile changes before selecting another record, applying filters, navigating, locking, or exiting. New contacts must be saved before adding related history. Archived records remain editable and readable, but must be restored before adding related history.

## Project structure

| Project | Responsibility |
| --- | --- |
| `src/HippoCrm.Core` | Models, authentication, SQLite schema, transactional services, audit trail |
| `src/HippoCrm.Desktop` | Avalonia application, navigation, editors, dashboard |
| `CRM.cs` | Desktop entry point, navigation, editors, and UI helpers; linked into the desktop project |
| `tests/HippoCrm.Tests` | Executable integration checks using temporary SQLite databases |
| `tests/HippoCrm.UiTests` | Headless desktop workflow checks and rendered screenshots |
| `docs/TEAM.md` | Independent teammate assignments and acceptance criteria |
| `scripts` | Repeatable build verification and publish commands |

Run the checks:

```powershell
dotnet run --project tests/HippoCrm.Tests
dotnet run --project tests/HippoCrm.UiTests
```

The UI checks exercise actual Avalonia controls against an isolated temporary database and write screenshots to `artifacts/screenshots`. They do not create accounts or example records in the application's normal data directory. `scripts/Verify.ps1` builds and runs both check suites. Native Windows/macOS installation and OS integration still require target-system verification.

## Security and operational boundaries

Passwords use PBKDF2-SHA256 with individual random 32-byte salts, 600,000 iterations, and fixed-time comparison. SQL values use parameters. Changes and audit entries commit in one transaction. Foreign keys and database triggers enforce retention during normal application/database use. A person with direct write access to the database file can bypass application controls or alter schema; OS file permissions remain essential.

The SQLite file is **not encrypted**. Authentication controls application access; it does not encrypt stored contact information. Use an appropriately protected OS account and full-disk encryption before entering sensitive real data. Database encryption/key management, multi-user administration, password recovery, screen-lock integration, and a security review remain release work. Do not distribute a pre-populated database or administrator credentials in source control.

## Releases

`scripts/Publish.ps1` publishes self-contained binaries for `win-x64`, `osx-x64`, or `osx-arm64`. The output is a portable application directory. Windows installation packaging and macOS `.app` packaging, signing, notarization, and native OS acceptance tests are tracked in `docs/TEAM.md`; publish output is not an installation package. Publish macOS releases on a Mac for packaging and validation.

Project milestones: MVP review October 31, 2026; target completion November 7, 2026. Media attachments, organization logos, release packaging, and remaining release hardening are assigned in the team plan.
