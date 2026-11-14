# Rust (KillRight engine)

## Ownership and types
- Take `&str` and `&[T]`, not `&String` or `&Vec<T>`; return owned values.
- Move out of a `&mut` with `mem::take` or `mem::replace` rather than cloning.
- Use newtypes or enums instead of bool parameters, sentinel values, or ids that can be mixed up.
- Use `let … else` for early returns.
- Contract types live in `contracts/`, with snake_case fields that match the C# DTOs.

## Errors and panics
- Expected failures are `Result` values that end as a `failure` code in the JSON response.
- Panic only on a bug; prefer `expect` with the reason it cannot fail over `unwrap`.
- No panic reachable from request data: no unchecked indexing, lossy `as` casts or unchecked arithmetic on input (use `get`, `try_from`, `checked_*`).
- Keep the cause: never a `map_err` that discards the original error.
- `Drop` implementations never panic or block.

## Concurrency
- Runtime state sits behind one `Mutex`; hold the guard only to clone what a call needs, never across analysis code.
- A poisoned lock is a bug: return a failure, never recover silently.
- Per-call data (timings) stays thread-local.

## Unsafe
- `unsafe` appears only in the `lib.rs` exports, as the smallest block around the pointer operation.
- Before a dereference, the pointer is non-null, aligned, initialised, not mutably aliased and valid for the whole use.
- Never `unsafe impl Send` or `Sync`, or `transmute`, to satisfy the compiler.

## Settings and the CLI
- Thresholds and other changeable values come from the settings the runtime loads, never from literals.
- The CLI should use the library crate rather than compiling the modules again through `#[path]`, and its thresholds should come from settings (BL-013); add no new literals there.
