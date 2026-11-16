# C# and Rust boundary

## What crosses
- Only `i32` and pointers to NUL-terminated UTF-8 strings; data travels as JSON. No `#[repr(C)]` structs, callbacks or handles.
- Requests and responses are the serde types in `contracts/` and their C# DTOs. A field change updates both sides and a round-trip test in the same package.

## Rust exports
- `#[no_mangle] pub extern "C"`, named `killright_…`.
- Null-check every pointer argument first and return a failure response for null.
- Wrap the whole body in `catch_unwind`; a panic returns `internal_error`. Never set `panic = "abort"`.
- Every response is JSON; a failure carries a stable snake_case `failure` code, and its cause goes in the JSON, not only to stderr (BL-012).

## Memory
- Rust allocates every returned string with `CString::into_raw`; only `killright_free_string` frees it.
- C# copies the request into `Marshal.AllocHGlobal` memory and frees it with `FreeHGlobal` in `finally`; it reads the response with `PtrToStringUTF8` and calls `killright_free_string` in `finally`.
- Never free Rust memory from C#, or C# memory from Rust.

## C# runtime
- All native access goes through `KillrightEngineRuntime`, one instance per process.
- Delegates are Cdecl and held in fields for the instance's lifetime.
- Native calls run off the UI thread (BL-011: they run on the caller's thread).
- The library is never freed while a call is running: count calls in flight and make `Dispose` wait (BL-007).
- A missing DLL or export fails cleanly: release the library handle and report `missing_runtime` (BL-010).
- A C# fallback response has the same JSON shape as the call it replaces (BL-010: the batch fallback differs).

## Versioning and tests
- The engine exports a contract version; the runtime checks it at load and refuses a mismatch (BL-009).
- Interop tests fail, never skip, when the DLL is missing (BL-008).

## Project specifics
- Request and response DTOs are mirrored by hand and match by name and type; neither side uses `deny_unknown_fields`, and optional fields are omitted when empty.
- Times cross as round-trip (`"O"`) ISO strings.
- Each export returns one JSON envelope with a `failure` field; the batch export returns an empty `results` list beside its failure.
- `killright_initialize` returns 1 or 0 and `killright_shutdown` returns nothing; neither returns JSON. `killright_free_string` ignores null.
- The runtime sets the engine's settings path before initialising; a settings failure must stop initialisation on both sides, not fall back silently.
