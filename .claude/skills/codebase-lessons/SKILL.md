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
- `killright_engine` builds its lib as `cdylib` + `rlib`; the CLI bin links the lib through `rlib`, and CLI integration tests in `tests/` run it via `CARGO_BIN_EXE_killright_engine_cli` (WS-1.3)

### Conventions
- {lesson} (WS-x.y)

### Gotchas
- zKillboard API calls (killmails, statistics, prices, posting, history dumps, R2Z2 feed): the official wiki https://github.com/zKillboard/zKillboard/wiki and its API sub-pages are the source; consult them before implementing or changing any zKill integration, never rely on assumptions (pre-framework)
- The engine dll location is resolved from `CARGO_TARGET_DIR`, falling back to `killright_engine/target` (`EngineTargetDir` in `Killright.UI.csproj`, same rule in `Build-Package.ps1`); never hard-code a cargo output path in a project, script or test (WS-1.1)
- `KillrightEngineRuntime` binds exports through `INativeLibrary.GetExport<T>`; test fakes must return delegates, not function pointers (`GetDelegateForFunctionPointer` hands back the original delegate type) (WS-1.3)
- `Killright.UI.csproj` copies the engine dll from the cargo output for the build configuration (`debug`/`release`); a missing dll fails the build with MSB3030, so build the engine in the matching profile (`--release` for Release) first (WS-1.3)
