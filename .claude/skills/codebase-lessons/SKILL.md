---
name: codebase-lessons
description: Curated, reusable lessons about this codebase (build quirks, conventions, gotchas) accumulated across work streams. Preloaded into package agents; read-only for them, curated by the Team Lead.
user-invocable: false
metadata:
  source: codebase-lessons template
---

## Codebase Lessons
**Rules for agents:**
- Apply these when relevant.
- Never edit this file; report new lessons in your handover's Lessons section.

**Rules for the Team Lead (curator):**
- One line per lesson, tagged with the source work stream.
- Keep only reusable, codebase-level lessons; remove superseded or duplicate lines.
- Keep the file under 80 lines.
- This file merges with `merge=union`, so dedupe at release.

### Build & Tooling
- In a worktree, a hook refuses Bash commands that combine `git` with heredocs or `&&` chains; run plain single commands and write files with the Write tool (WS-1.2)
- Without a prior `dotnet restore KillRight/KillRight.sln`, `dotnet test --no-restore` can exit 0 with no output; check the test count, not the exit code (WS-1.2)

### Conventions
- {lesson} (WS-x.y)

### Gotchas
- SQLite `PRAGMA data_version` is per connection and cannot detect changes across runs; whole-database change detection hashes table content (WS-1.2)
- zKillboard API calls (killmails, statistics, prices, posting, history dumps, R2Z2 feed): the official wiki https://github.com/zKillboard/zKillboard/wiki and its API sub-pages are the source; consult them before implementing or changing any zKill integration, never rely on assumptions (pre-framework)
- The engine dll location is resolved from `CARGO_TARGET_DIR`, falling back to `killright_engine/target` (`EngineTargetDir` in `Killright.UI.csproj`, same rule in `Build-Package.ps1`); never hard-code a cargo output path in a project, script or test (WS-1.1)
- `Killright.UI.csproj` copies the engine dll from the cargo output and silently skips a missing file; check the app output folder for `killright_engine.dll` rather than trusting a green build (WS-1.1)
