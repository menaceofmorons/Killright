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
- In a worktree, a hook refuses Bash commands that use heredocs or `&&` chains, with or without `git`; run plain single commands and write files with the Write tool (WS-1.2, WS-1.6)
- Without a prior `dotnet restore KillRight/KillRight.sln`, `dotnet test --no-restore` can exit 0 with no output; check the test count, not the exit code (WS-1.2)
- `killright_engine` builds its lib as `cdylib` + `rlib`; the CLI bin links the lib through `rlib`, and CLI integration tests in `tests/` run it via `CARGO_BIN_EXE_killright_engine_cli` (WS-1.3)

### Conventions
- {lesson} (WS-x.y)

### Gotchas
- `KillRightDatabase.WriteGate` is static and shared by every instance, so parallel tests can hold it; tests that wait on it need a generous timeout (WS-1.2)
- zKillboard API calls (killmails, statistics, prices, posting, history dumps, R2Z2 feed): the official wiki https://github.com/zKillboard/zKillboard/wiki and its API sub-pages are the source; consult them before implementing or changing any zKill integration, never rely on assumptions (pre-framework)
- The engine dll location is resolved from `CARGO_TARGET_DIR`, falling back to `killright_engine/target` (`EngineTargetDir` in `Killright.UI.csproj`, same rule in `Build-Package.ps1`); never hard-code a cargo output path in a project, script or test (WS-1.1)
- `KillrightEngineRuntime` binds exports through `INativeLibrary.GetExport<T>`; test fakes must return delegates, not function pointers (`GetDelegateForFunctionPointer` hands back the original delegate type) (WS-1.3)
- `Killright.UI.csproj` copies the engine dll from the cargo output for the build configuration (`debug`/`release`); a missing dll fails the build with MSB3030, so build the engine in the matching profile (`--release` for Release) first (WS-1.3)
- xunit test classes that initialise the real engine share one `[Collection("RealEngine")]`; the engine runtime is process-wide, so parallel classes fail intermittently (WS-1.4)
- Group Detection skips pairs whose current identities share a corporation; seeded test pilots need distinct corporation ids (WS-1.4)
- A stub `HttpMessageHandler` that returns synchronously ignores a pre-cancelled token; cancellation tests need a handler that awaits the token (WS-1.5)
- `HttpListener` on `127.0.0.1` runs in tests without admin rights and gives a real HTTP round trip (e.g. decompression checks) (WS-1.5)
- `ScanPipelineSourceTests` asserts on `MainWindow.xaml.cs` text; moving code out of `MainWindow` can break it (WS-1.5)
- ID-based rules on a pilot use `Pilot.CorporationId`; the `Corporation` object is present only when the name resolved (WS-1.5)
- Tests that call `AppearanceManager.ApplyTheme` or `ApplyNewPilotColor` mutate the shared `Application.Resources`; put them in a serialised collection (`DisableParallelization = true`) and restore merged dictionaries, or theme-brush tests fail intermittently (WS-1.7)
- A new UI test never asserts on `.xaml.cs` source text; put the logic in a testable class and assert its behaviour (WS-1.7)
- A modal startup dialog blocks process-exit integration tests; the test must find and dismiss it (user32 `EnumWindows`) before asserting exit (WS-1.7)
- An unshown WPF window can be laid out in tests by measuring and arranging its content; the custom `TabControl` template has no named content host, so find the row 1 `Border` of its `Grid` (WS-1.8)
- Test seed rows timestamped with SQL `unixepoch()` ignore `ApplicationClock`; take seed times from the application clock (WS-1.Integration)
