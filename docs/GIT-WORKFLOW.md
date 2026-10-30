# Visual Studio and GitHub workflow

On this computer, use this solution as the primary working copy:

`C:\Users\selen\source\repos\SVargas42\CRM-Project\HippoCrm.slnx`

The project folder under `OneDrive\Desktop\Coding\CRM Project` is a separate Git checkout. Changes in one checkout do not automatically appear in the other. Work in the Visual Studio checkout consistently instead of copying source files between them.

## Before editing

In Visual Studio's Git Changes window, confirm the repository is **CRM-Project** and select the intended branch. Use **Fetch**, then **Pull** to bring down committed changes. `main` tracks `origin/main` in `SVargas42/CRM-Project`.

Fetch updates Git's knowledge of GitHub; Pull updates local files. A push uploads committed changes. Unsaved editor buffers and uncommitted files are not shared by either operation. Save files and reload files changed on disk when Visual Studio prompts.

## Small updates

1. Make one focused change, usually involving 1–5 related files.
2. Run relevant checks and review the diff.
3. Stage only those files and give the commit a clear description.
4. Push the commit before moving on to an unrelated change.

Avoid 20-plus-file uploads. Break larger features into working, reviewable increments. Do not split files arbitrarily when doing so would leave a broken build.

Local configuration uses fast-forward-only pulls and simple pushes. If a pull reports divergence, inspect both histories and preserve local work before resolving it. Do not use force-push or discard changes to make an error disappear.

## Preserved pre-sync work

On October 30, 2026, older uncommitted updates were preserved in local stashes in both checkouts before bringing their `main` branches up to date. View them through Visual Studio's stash support or `git stash list` in the corresponding repository. They are local recovery copies, not commits uploaded to GitHub.

Do not apply the entire older stash over current main: GitHub contains newer security and UI changes that overlap it. Recover any still-needed features individually on a separate branch after reviewing the current implementation.

Changes pushed only to `test1-branch` remain on that branch until merged. Switching to main does not include unmerged branch work.
