# KillRight — Project Instructions

## File access failures

If a file fails to open/read/write (e.g. locked, in use by another process), retry once. If the second attempt also fails, stop and prompt the user rather than continuing to retry — it likely means the user has the file open elsewhere (e.g. in Word or Excel) and needs to close it.

## zKillboard API

Whenever zKillboard (zKill) API calls are required — killmails, statistics, prices, posting killmails, history dumps, or the R2Z2 real-time feed — the definitive source is the official zKillboard GitHub wiki: https://github.com/zKillboard/zKillboard/wiki

Consult it (and its sub-pages: API (Killmails), API (History), API (Posting Killmails), API (Prices), API (R2Z2), API (Statistics)) before implementing or modifying any zKill integration, rather than relying on assumptions or outdated knowledge.

## Implementation Guide generation

When the user requests:

```
Create IG <step number>
```

before generating the guide:

- Review the current standards: `Documentation/KR-Standards-Implementation-Guide-v#.#.docx` (use the highest version number present).
- Review the current design: `Documentation/KillRight-Design-Specification-v#.#.docx` (use the highest version number present).
- Enforce all standards and design requirements found in those documents.

### Save location

Every Implementation Guide `.md` file — standard, CC, or any other category — is saved directly in `Documentation/Implementation Guides/` when first generated, not in any subfolder (`Executed/`, `Check/`, `Archive/`). Guides only move into those subfolders later, per the Tracker and Lifecycle process below.

### CC IG naming

When the user requests:

```
Create CC IG
```

follow the same Implementation Guide generation process above, but name the guide `CC-DD.MM.YY.##` instead of the standard sequential number, where:

- `DD` is the current day of month (leading zero if single digit)
- `MM` is the current month (leading zero if single digit)
- `YY` is the last two digits of the current year
- `##` is the sequential count of CC IGs requested that same day (starting at `01`)

## Implementation Guide execution

When the user requests:

```
Execute IG <name or step number>
```

locate the matching file in `Documentation/Implementation Guides/` (exact filename if given, otherwise the closest match by step number or description) and follow it exactly per the current `KR-Standards-Implementation-Guide-v#.#.docx` (use the highest version number present):

- Perform Git Preparation, the Changes section, Build Verification, and every test step marked "Executable By: Agent" directly, without asking for confirmation between them.
- Stop before any step marked "Executable By: Developer". Report that it is ready for the developer to run manually, along with what remains, and do not attempt it.
- Follow the standards' Failure Handling section if any step does not produce its stated Expected Result: stop, do not self-remediate or silently retry, and report exactly what ran and what was expected instead.
- Do not run Git Closeout until the developer has confirmed all Developer-only tests have passed.
- Guide execution ends at Git Closeout (the guide's own final section). Once Git Closeout completes, report success and stop. Do not update `Implementation-Guides-Tracker.xlsx`, and do not move the guide file to `Executed/`, `Archive/`, or anywhere else. Those are a separate process (see "Implementation Guide Tracker and Lifecycle" below) and are never an automatic continuation of executing a guide, even after the developer confirms it succeeded.

## Implementation Guide Tracker and Lifecycle

This is a distinct request from "Execute IG X" — never trigger it automatically after finishing a guide's execution, including its Git Closeout. Only act on it when the developer separately and explicitly says a guide is ready to execute, has succeeded, has failed, or needs a REV, as its own instruction.

When that happens, follow `KR-Standards-Implementation-Guide-v#.#.docx` Section 9 (Implementation Guide Tracker and Lifecycle) — use the highest version number present. That section is the authoritative definition of the tracker's columns and Status values, and of exactly when `Implementation-Guides-Tracker.xlsx` is updated and a guide file is moved between `Documentation/Implementation Guides/`, `Executed/` and `Archive/`. Do not duplicate that process here or improvise a different one.

## Documentation/Archive folder

`Documentation/Archive/` holds superseded versions of standards, design specs, and other docs (old `PI-Implmentation-Guide-Standards`, old `PilotIntel-Design-Specification`/`KillRight-Design-Specification` versions, etc.). Ignore everything in this folder — do not read, cite, or apply anything from it as current guidance — unless the user explicitly names a file in it and asks you to use it. Always work from the highest version number present outside `Archive/`.

## Known issues and gotchas

### DuckDB (duckdb-rs): a dropped `Connection` does not release its file handle if any `Statement`/`Rows`/`Appender` from it is still alive

Dropping (or explicitly closing) a `duckdb::Connection` does **not** release the underlying OS file handle if any `Statement`, `Rows` cursor, or `Appender` prepared from that connection is still an un-dropped local variable elsewhere in the same function. `duckdb-rs` allows the `Connection` to be dropped first — it compiles cleanly and the close itself reports success — but the still-alive, un-finalized handle keeps DuckDB's internal reference to the file open for the rest of the *process's* life (not a brief delay; it persists until the process exits). A same-process reopen of that file then fails indefinitely with "file is already open in ...exe (PID <this same process>)", and no retry, backoff, or spawning a separate child process to do the reopen will fix it, because the parent process genuinely never released the file.

This is easy to misdiagnose as a Windows/DuckDB asynchronous handle-release timing issue, especially since the error names the current process's own PID either way. The real signal is that a **completely separate, independently-launched process** (e.g. the DuckDB CLI, or Python's `duckdb` package) can open the identical file immediately once the writer process has actually exited — if that succeeds while the writer process is still running and blocked on its own attempted reopen, the lock is internal to that process, not the file.

**Pattern to follow:** scope every `Statement`, `Rows`, and `Appender` in its own block (or otherwise explicitly `drop()` them) before running anything — `CHECKPOINT`, closing the connection, or reopening the same file — that depends on the connection being fully, truly closed. This surfaced in IG 19.00.47's validation gate (`clean_reopen` check) and cost four revision cycles (REV-A through REV-D) to isolate. If a "file already open" error names this process's own PID after code that prepared a query without explicitly finalizing it, check for this pattern first before assuming a timing race.

### Implementation Guides: verify Test Expected Results against the Overview's own documented behavior

When a guide's Overview section documents specific derived or filtered behavior (e.g. "returns only days not already Completed"), trace each Test's Expected Result through that actual logic before publishing — don't default to the "obvious" reporting shape (e.g. "requested = full horizon, already-completed counted downstream") without checking it against what the code you just specified actually does. IG 19.00.47 shipped with three tests (5.2, 5.3, 6.1) whose Expected Results assumed a different filtering design than the one its own Overview described and `determine_missing_days` implemented; the mismatch went undetected until those tests actually ran (REV-E).
