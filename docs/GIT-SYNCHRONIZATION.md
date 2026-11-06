# Team Git synchronization and merge checks

Run these commands from your own CRM-Project checkout. Visual Studio and VS Code are editors of that folder, not separate upload destinations. Saving writes local files; committing records history; pushing sends those commits to GitHub. Fetch updates remote references; pulling integrates changes into the checked-out branch. A release executable can lag behind its source branch.

## What this review found (November 6, 2026)

At the start of this work, origin/main was 1a65f4e, analytics-test was b11e992, test-main was 9466207, and test1-branch was d692954. Local main was 48 commits behind origin/main. Git's ancestry check showed origin/main was an ancestor of analytics-test (0 main-only / 67 analytics-only commits), but test1-branch had diverged (1 main-only / 34 feature-only commits). These are real unmerged histories, not just a GitHub refresh problem. The photo/duplicate implementation on test1-branch is not included in analytics-test; do not overwrite either branch with files from the other.

This snapshot becomes stale as teammates push. Re-run the commands below. No branches were merged into main as part of preparing this guide.

## 1. Identify the actual checkout, branch and tracking

```powershell
git rev-parse --show-toplevel
git remote -v
git status --short --branch
git branch -vv
git fetch origin
git branch -r
git log --oneline --decorate --graph --all -20
git rev-list --left-right --count main...origin/main
git rev-list --left-right --count origin/main...origin/analytics-test
git branch -r --merged origin/main
git branch -r --no-merged origin/main
```

Counts mean left-only and right-only commits. Inspect the selected branch in GitHub's branch selector and Visual Studio's Git Repository window. Confirm everyone uses the same owner/repository remote. Fetching alone does not update a local checkout.

Before switching or integrating, save editor buffers and preserve pending work. Commit reviewed source files, or stash only explicitly named source paths:

```powershell
git diff -- src/path/File.cs tests/path/Check.cs
git stash push --include-untracked -m "Preserve source before synchronization" -- src/path/File.cs tests/path/Check.cs
git stash list
```

Replace these example paths with actual source files. Do not include databases, keys, credentials, logs, diagnostics or private configuration. Do not use blanket `git add .`, discard unreviewed work, or apply an entire old stash over newer security code. Reapply selected work after reviewing the current implementation.

## 2. Update local main without rewriting shared history

Start with preserved work and a clean working tree:

```powershell
git fetch origin
git switch main
git branch --set-upstream-to=origin/main main
git pull --ff-only origin main
```

If fast-forward fails, stop and inspect `git log --left-right --oneline main...origin/main`. Local and remote histories diverged. Preserve both; resolve deliberately with an ordinary merge or a reviewed pull request. Do not reset main or force-push to make the message disappear.

## 3. Integrate a shared feature branch (recommended team workflow)

Example for analytics-test, after its tests and review pass:

```powershell
git fetch origin
git switch main
git pull --ff-only origin main
git diff --stat main...origin/analytics-test
git merge --no-ff origin/analytics-test -m "Merge reviewed analytics and keyboard improvements"
# Run the relevant offline build, UI and security suites here.
git push origin main
```

Use the corresponding feature branch name when integrating another branch. Where main is protected, push the feature branch and use GitHub's Compare & pull request targeting main; complete the required checks and use the team's approved merge method. Do not bypass protection.

For conflicts, inspect `git status`, resolve each file preserving both contributors' behavior, stage explicit resolved paths, and run tests:

```powershell
git add -- src/path/ResolvedFile.cs
git merge --continue
# If abandoning the in-progress merge instead:
# git merge --abort
```

The diverged branches contain security, UI and schema work. Matching schema version numbers do not prove matching layouts: test1's image/link upgrade and test-main's normalized skills/tags evolution need an explicitly reviewed compatibility path. Do not deploy a combined build against an existing workspace until encrypted recovery and upgrade tests pass. Keep real data out of Git.

## 4. Rebase only personal, unpublished work

Rebase moves commits onto a newer base; it does not itself merge a feature into main. For a personal branch never shared with teammates:

```powershell
git fetch origin
git switch codex/my-local-feature
git rebase origin/main
# Resolve conflicts, then:
# git add -- src/path/ResolvedFile.cs
# git rebase --continue
# Or cancel: git rebase --abort
git switch main
git pull --ff-only origin main
git merge --ff-only codex/my-local-feature
# Run the agreed checks.
git push origin main
```

Prefer an ordinary merge for shared test1-branch, test-main and analytics-test. Rewriting an already-published feature forces teammates to reconcile different commit IDs and can make merge status more confusing. Do not force-push shared branches.

## 5. Verify the result and tracking

```powershell
git fetch origin
git log -1 --oneline main
git log -1 --oneline origin/main
git rev-list --left-right --count main...origin/main
git merge-base --is-ancestor origin/analytics-test origin/main
# Exit code 0 means this feature tip is contained in remote main.
git branch -r --merged origin/main
```

If a feature has no upstream, set it explicitly (`git branch --set-upstream-to=origin/analytics-test analytics-test`) or use `git push -u origin analytics-test` when publishing the intended local branch. This fixes tracking, not an unperformed merge. A rejected ordinary push usually means newer remote work or branch protection: fetch, inspect, integrate safely and test again; do not force-push.

Git's --merged/ancestor checks follow commit ancestry. A squash merge or cherry-pick can copy changes without containing the original feature tip, so that tip may still appear unmerged in Git even when a GitHub PR was squash-merged. Check the PR's merged status and the resulting main diff/commit. A deleted branch name, a local commit, or a successful release build is not evidence that its work reached main.
