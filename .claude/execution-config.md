# Execution Config

Execution settings for the agent team. It lives **in the repo** at `.claude/execution-config.md`, so every worktree has it at the same relative path, and changes to commands are versioned and released with the code. It is read by the Team Lead (`#T-TLO`), the package agents and `health-check`.

Project variables (`#PN`, `#DP`, `#CP`) are **not** defined here; they come from `#NP`.

Every command row names in `Requires` the repo-relative file whose presence makes the command valid (e.g. `Cargo.toml`, `ui/package.json`), or `—` if it always applies. How a row whose file is absent is treated: `project-rules` (Execution Config rows).

One check per row. A row has one `Requires` path, so a command spanning two parts of the stack is split into two rows. Chain with `&&` only where a later step depends on an earlier one (e.g. generate, then diff), so one failure never hides another check's result.

## Settings
| Var | Value | Notes |
| --- | --- | --- |
| `#GR` | `develop` | Integration target branch |
| `#REMOTE` | `true` | `true` to fetch, pull and push `origin` |
| `#MAX_PARALLEL` | `2` | Max package agents running at once per Team Lead (throttle for plan usage limits) |

## Tracker Columns (`py "{#TOOLS}/trkr_update.py"`)
| Arg | Header in `#TRKR` |
| --- | --- |
| `--id-col` | `ID` |
| `--status-col` | `Status` |
| `--completed-col` | `Completed` |
| `--notes-col` | `Notes` |

The tab is always `--sheet #WS_ID`.

## Worktree Setup
Run by every agent in a fresh worktree, and by `health-check` before checks. Commands must be idempotent and quick when nothing has changed (prefer `npm install` to `npm ci`). Missing local files belong in `.worktreeinclude`.

| Step | Command | Requires |
| --- | --- | --- |
| deps-backend | `cargo fetch --manifest-path killright_engine/Cargo.toml` | `killright_engine/Cargo.toml` |
| deps-app | `dotnet restore KillRight/KillRight.sln` | `KillRight/KillRight.sln` |

## Build Cache (health-check, pkg-integrator)
Every worktree builds in its own checkout, so a toolchain that keeps its build output or cache per checkout starts cold in each one. One row per compiled toolchain. `Redirect` is what the Developer sets once per machine (`Machine-Setup.md`, Build caches): a machine-wide folder outside any synced folder. `Check` is a command that exits 0 when the redirect is in effect. Rules: `project-rules` (Build cache).

| Toolchain | Redirect | Check | Requires |
| --- | --- | --- | --- |
| Rust | user variable `CARGO_TARGET_DIR` set to a folder outside any synced folder (`C:/BuildCache/cargo`) | `py -c "import os, sys; sys.exit(0 if os.environ.get('CARGO_TARGET_DIR') else 1)"` | `killright_engine/Cargo.toml` |

## Package Test Commands (package agents / test-runner)
| Purpose | Command | Requires |
| --- | --- | --- |
| Build (engine) | `cargo build --manifest-path killright_engine/Cargo.toml` | `killright_engine/Cargo.toml` |
| Build (app) | `dotnet build KillRight/KillRight.sln --no-restore` | `KillRight/KillRight.sln` |
| Unit tests (engine) | `cargo test --manifest-path killright_engine/Cargo.toml` | `killright_engine/Cargo.toml` |
| Unit tests (app) | `dotnet test KillRight/KillRight.sln --no-restore` | `KillRight/KillRight.sln` |
| Unit tests (filtered) | `dotnet test KillRight/KillRight.sln --no-restore --filter "{filter}"` | `KillRight/KillRight.sln` |

## Health Checks (health-check)
Scopes: `wave` per T-TLO §2.6; `full` per §2.7 and §3. A check must pass on a clean checkout.

| Name | Command | Requires | Scope |
| --- | --- | --- | --- |
| build-engine | `cargo build --manifest-path killright_engine/Cargo.toml --all-targets` | `killright_engine/Cargo.toml` | wave, full |
| build-app | `dotnet build KillRight/KillRight.sln --no-restore` | `KillRight/KillRight.sln` | wave, full |
| lint-engine | `cargo clippy --manifest-path killright_engine/Cargo.toml --all-targets -- -D warnings` | `killright_engine/Cargo.toml` | wave, full |
| format-engine | `cargo fmt --manifest-path killright_engine/Cargo.toml --check` | `killright_engine/Cargo.toml` | wave, full |
| format-app | `dotnet format whitespace KillRight/KillRight.sln --verify-no-changes` | `KillRight/KillRight.sln` | wave, full |
| unit-engine | `cargo test --manifest-path killright_engine/Cargo.toml` | `killright_engine/Cargo.toml` | full |
| unit-app | `dotnet test KillRight/KillRight.sln --no-restore` | `KillRight/KillRight.sln` | full |
| settings-drift | `dotnet test KillRight/Killright.Settings.Tests/Killright.Settings.Tests.csproj --no-restore` | `KillRight/Killright.Settings.Tests/Killright.Settings.Tests.csproj` | wave, full |

## Release (`#T-REL`, `#T-RS`)
| Var | Value | Notes |
| --- | --- | --- |
| `#BUILD` | `local` | `actions`: `#RWF` builds and publishes on tag push (public GitHub repositories); `local`: `#T-REL` builds and runs the publish steps |

### Version Files
| File | Pattern |
| --- | --- |
| `Packaging/Build-AlphaPackage.ps1` | `$version = '{v}'` |

### Release Build (`local` only)
| Step | Command |
| --- | --- |
| package | `powershell -File Packaging/Build-AlphaPackage.ps1` |

Artefacts: `Packaging/out/`

### Publish Steps
| Step | Command or `manual` |
| --- | --- |
| GitHub Release | `manual` |
| announce | `manual` |

_Source: Execution-Config-Template_
