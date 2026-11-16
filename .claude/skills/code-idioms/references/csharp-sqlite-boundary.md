# C# and SQLite boundary

## Ownership
- The Storage project reaches the database only through `Microsoft.Data.Sqlite`; one factory opens every connection.
- Connections, commands and readers use `using var`. A write scope owns its transaction and, if it opened the connection, the connection.
- Pooling stays on, except for read-only checks.

## Writes
- Every write goes through `BeginWrite` and ends in an explicit `Commit`; the scope rolls back on dispose unless committed.
- Every command is given the scope's transaction.
- Writes are serialised: a static `SemaphoreSlim` gate plus a non-deferred transaction.
- Batched writes and `IN` lists use chunks of 500 (2000 for the SDE tables).

## Encoding
- Read nullable columns through the `GetNullable*` helpers; never read a nullable column with a non-null reader.
- Parse stored enum text with `TryParse`, so a renamed member does not throw on read.
- Times leave Storage for the engine as round-trip strings (`"O"`); sub-second precision is dropped on the way in.

## Errors
- Catch `SqliteException` only where there is a specific recovery (corruption quarantine); other storage failures throw to a caller that records them.
- Readers that feed the engine catch all failures and return a failure reason instead of throwing.

## Threading
- Only writers take the gate; readers do not.
- A write batch commits as one transaction, so a failed scan leaves no partial rows.
