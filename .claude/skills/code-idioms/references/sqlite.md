# SQLite (KillRight)

## Project specifics
- One connection factory applies the per-connection PRAGMAs: foreign keys on, synchronous NORMAL, a busy timeout, cache size, and in-memory temp store.
- WAL is set when the file is created and checked on every open; new files use incremental auto-vacuum.
- Every table is `STRICT`, created `IF NOT EXISTS` with a `main.` prefix; a test fails if one is not `STRICT`.
- Keys are natural; no `AUTOINCREMENT`. Indexes are named `ix_{table}_{column}`.
- Booleans are `INTEGER` with a `CHECK` limiting them to 0 and 1; times are `INTEGER` Unix seconds everywhere; enums are `TEXT` member names.
- Schema version and the Alpha lock live in a `schema_metadata` table, not `user_version`.
- Backup is `VACUUM INTO` a `.tmp` file, then a move; restore and quarantine move the `-wal` and `-shm` files with the database.
- Tests use a temp-file database per test.
- `PRAGMA data_version` is per connection and cannot show changes between runs; detect a whole-database change by hashing table content.
