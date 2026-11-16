---
name: code-idioms
description: Per-language idioms, conventions and language-boundary rules for this codebase, written by the Developer. Preloaded into package agents and the code reviewer; read-only for them.
user-invocable: false
metadata:
  source: code-idioms template
---

## Code Idioms
**Rules for agents:**
- Before changing files a Languages row matches, read its reference file; when a change touches a Boundaries row, read that file too. The files are in this skill's `references/` folder (`.claude/skills/code-idioms/references/`).
- `project-rules` wins where the two differ.

**Rules for the Developer (author):**
- Idioms, conventions and tool preferences only; build, test and lint commands stay in the Execution Config.
- Paraphrase outside sources; never copy their text or examples.
- Keep this body under 60 lines and each reference file under 150 lines.
- A `{placeholder}` row means no guidance yet.

### Languages
| Language | Globs | Reference |
| --- | --- | --- |
| C# | `KillRight/**/*.cs`, `KillRight/**/*.xaml` | `references/csharp.md` |
| Rust | `killright_engine/**/*.rs` | `references/rust.md` |
| SQLite | `KillRight/Killright.Storage/**/*.cs` | `references/sqlite.md` |

### Boundaries
| Between | Files | Reference |
| --- | --- | --- |
| C# and Rust | `KillRight/Killright.UI/Analysis/KillrightEngineRuntime.cs`, `KillRight/Killright.UI/Analysis/RustRecentStyleClient.cs`, `KillRight/Killright.Storage/Engine/EngineInputModels.cs`, `killright_engine/src/lib.rs`, `killright_engine/src/contracts/**` | `references/csharp-rust-boundary.md` |
| C# and SQLite | `KillRight/Killright.Storage/**/*.cs`, `KillRight/Killright.Shared/Data/**` | `references/csharp-sqlite-boundary.md` |

### All languages
- Add a NuGet package or crate only when the brief names it.
- A rule marked with a Backlog id (`BL-nnn`) is not yet met by the code: follow it in new code, and change existing code only when the brief names that row.
- Rust `// SAFETY:` comments are allowed only while clippy's `undocumented_unsafe_blocks` lint is on for the engine (a tooling directive under `project-rules`); otherwise unsafe blocks carry no comment.
