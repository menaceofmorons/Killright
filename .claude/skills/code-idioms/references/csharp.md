# C# (KillRight)

## Types and layout
- File-scoped namespaces; the namespace matches the folder path, letter case included.
- Classes and records are `sealed` unless built for inheritance.
- Models are records with `required` and `init` members.
- Private fields are `_camelCase`; braces on their own lines.

## Nullable
- Nullable is on in every project; never turn it off with `#nullable`.
- Check a nullable value once where it enters, then keep it non-null (`is not { } value`, early return).
- Use `!` only where an outside guarantee the compiler cannot see makes null impossible; never to silence a warning.
- Public entry points that take references call `ArgumentNullException.ThrowIfNull`.
- Return `T?` only when absence is a normal outcome; where callers need both, pair a throwing `Get…` with a `TryGet…`.
- C# 14 features (`field`, `?.` on the left of an assignment) only once every project targets net10.0 or later.

## Async and threading
- Every async method takes `CancellationToken cancellationToken = default` and passes it on.
- Never block on a task (`.Result`, `.Wait()`, `GetAwaiter().GetResult()`) on the UI thread; the startup and shutdown waits in `App.xaml.cs` are the only exceptions.
- Independent work runs with `Task.WhenAll`; producer and consumer work uses a bounded `Channel<T>`; `lock` and `SemaphoreSlim` come last.
- CPU-bound and native work runs off the UI thread; results come back through `Dispatcher.InvokeAsync`, never `Dispatcher.Invoke`.
- UI polling uses `DispatcherTimer`, not `System.Timers.Timer`.
- No `ConfigureAwait(false)`: the UI code needs its context.

## WPF
- `async void` only for event handlers; each wraps its body in try/catch and records the failure.
- No fire-and-forget task without a continuation that observes its exception.
- `App.xaml.cs` handles dispatcher, AppDomain and `TaskScheduler.UnobservedTaskException` failures (BL-014: the last is missing).
- View models raise `PropertyChanged` through one shared `SetField` helper (equality check, `[CallerMemberName]`); never write another.
- Code-behind wires events and calls view models or services; new logic goes in a view model or service, not in `*.xaml.cs`.
- User-visible text comes from `UiText`.

## Errors
- Expected failures return result values (`null`, `Failed(…)`) and are recorded with `EngineFailureLog.Record`; exceptions are for bugs and broken invariants.
- Catch the specific exception type. A bare `catch` or `catch (Exception)` only rethrows after cleanup, or sits at a boundary (event handler, background loop, native call) that records the failure.
- Never an empty catch.

## Resources and native calls
- Disposables use `using` or `using var`.
- Native handles (GDI, window, library) are released in `finally` or `Dispose`.
- New P/Invoke declarations use `[LibraryImport]` with `nint` (the project needs `AllowUnsafeBlocks`); check return values, and read `Marshal.GetLastPInvokeError()` when the API sets the last error.

## Tests
- Follow `dev-testing`. Here: one `public sealed {Type}Tests` class per type; methods named `Subject_Condition_Result`; hand-written fakes, no mocking library; a temp-file SQLite database per test; WPF objects only inside `Sta.Run`.
- Assert on behaviour, never on source text.

## Project specifics
- Statics and constants are PascalCase.
- Instants are `DateTimeOffset` in UTC; elapsed time uses `Stopwatch.GetTimestamp` and `GetElapsedTime`, never wall-clock subtraction.
- Time comes from an injected `Func<DateTimeOffset>`; app-wide code uses the static `ApplicationClock`. Never call `DateTimeOffset.UtcNow` in logic that tests need to control.
- Catch cancellation from the caller's own token with a `when` filter on that token, so other cancellations still surface.
- Locks use a private `_gate` object; enum `switch` expressions throw in the discard arm.
- No `ICommand` and no MVVM toolkit: views use `Click=` handlers in XAML and thin view models.
- Theme brushes and font tiers are `DynamicResource` dictionaries, swapped in `Application.Resources.MergedDictionaries`.
- A bound grid collection is replaced in bulk on the UI thread, not changed item by item.
- Composition is manual: `App.OnStartup` builds services into static `App` properties; no DI container.
- `OnExit` runs shutdown as named steps, each guarded on its own, so one failure does not skip the rest.
- Tests use xunit only.
