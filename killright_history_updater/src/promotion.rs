use std::fs;
use std::path::{Path, PathBuf};

use duckdb::{Connection, Result as DuckResult};

use crate::folder_layout;

/// Given a Working-folder file name like
/// `CORE.KillRight.History.260810.01.duckdb`, produces the corresponding
/// Live-folder promoted name: the same `yymmdd.##` versioned suffix, without
/// the `CORE.` prefix (matches the C# project's
/// `HistoryUpdaterStagingPaths.PromotedFileSearchPattern`, declared at
/// 19.00.55 for this exact purpose). Returns `None` if `working_file_name`
/// does not start with the expected prefix.
pub fn promoted_file_name(working_file_name: &str) -> Option<String> {
    working_file_name.strip_prefix("CORE.").map(|name| name.to_string())
}

/// Copies the finished Working-folder file to its promoted name in Live, via
/// a plain OS-level file copy. The caller is responsible for checkpointing
/// and closing the Working connection first (see `staging::build_staging`):
/// `CHECKPOINT` merges the write-ahead log into the main database file, so a
/// plain file copy captures everything, and the connection must be fully
/// closed so nothing else has the file open while it's being read. Design
/// Specification v5.4 Section 6.9.4: "it is copied under its own versioned
/// name ... into the live folder."
pub fn copy_to_live(root: &Path, working_file_name: &str) -> Result<PathBuf, String> {
    let promoted_name = promoted_file_name(working_file_name).ok_or_else(|| {
        format!("Working file name '{working_file_name}' does not start with the expected CORE. prefix")
    })?;

    let live_directory = folder_layout::live_dir(root);
    fs::create_dir_all(&live_directory).map_err(|error| format!("Failed to create Live directory: {error}"))?;

    let working_path = folder_layout::working_dir(root).join(working_file_name);
    let live_path = live_directory.join(&promoted_name);

    fs::copy(&working_path, &live_path).map_err(|error| format!("Failed to copy working file to Live: {error}"))?;

    Ok(live_path)
}

/// Moves a Live-folder copy that failed validation into Failed, via a plain
/// OS-level rename (Live and Failed are always sibling subfolders of the
/// same historic database root, so this never crosses a filesystem
/// boundary). This is 19.00.56's own share of the plan's "the copy is handed
/// to 19.00.58 rather than being left in the Live folder or silently
/// discarded," and of Design Specification Section 6.9.4's "a copy that
/// fails validation is moved to a separate failed-build folder" -- 19.00.58
/// (Failed Build Handling) adds user notification on top of this move, not
/// the move itself.
pub fn move_to_failed(root: &Path, live_path: &Path) -> Result<PathBuf, String> {
    let failed_directory = folder_layout::failed_dir(root);
    fs::create_dir_all(&failed_directory).map_err(|error| format!("Failed to create Failed directory: {error}"))?;

    let file_name = live_path.file_name().ok_or_else(|| "Live-folder copy path has no file name".to_string())?;
    let failed_path = failed_directory.join(file_name);

    fs::rename(live_path, &failed_path)
        .map_err(|error| format!("Failed to move the failed promotion copy to Failed: {error}"))?;

    Ok(failed_path)
}

/// Adds the promotion-time uniqueness constraints Design Specification v5.4
/// Section 4.7 documents per table, to a Live-folder copy's connection only.
///
/// Uses `CREATE UNIQUE INDEX`, not `ALTER TABLE ... ADD PRIMARY KEY`: DuckDB
/// (this crate's `duckdb` dependency, version 1.x) does not support adding a
/// primary key to an already-existing table via `ALTER TABLE` -- confirmed
/// against duckdb/duckdb issues #15190, #15821, and #15835 ("No support for
/// that ALTER TABLE option yet!"), all open at the time of this guide. A
/// unique index is DuckDB's own supported mechanism for enforcing uniqueness
/// on an existing table (composite/multi-column indexes are supported), and
/// serves this guide's purpose exactly: `CREATE UNIQUE INDEX` itself fails
/// if the table already contains duplicate values in the indexed column(s)
/// -- the full-table duplicate-evidence check Section 6.9.2/6.9.4 call for,
/// folded into the validation gate rather than a separate step.
///
/// One index per primary key Section 4.7 documents for these four tables:
/// `historic_relationship_evidence` (`evidence_id`, plus a second unique
/// index for its documented `source_killmail_id` uniqueness),
/// `historic_relationship_evidence_participants` (`evidence_id`,
/// `character_id`), `historic_relationship_summary` (`pilot_a_id`,
/// `pilot_b_id`), `historic_relationship_org_context` (`pilot_a_id`,
/// `pilot_b_id`). Never applied to the working database -- see
/// `schema.rs::create_schema`'s doc comment (19.00.55).
pub fn add_promotion_constraints(connection: &Connection) -> DuckResult<()> {
    connection.execute_batch(
        "CREATE UNIQUE INDEX pk_hre_evidence_id ON historic_relationship_evidence(evidence_id);
         CREATE UNIQUE INDEX uq_hre_source_killmail_id ON historic_relationship_evidence(source_killmail_id);
         CREATE UNIQUE INDEX pk_hrep_evidence_character ON historic_relationship_evidence_participants(evidence_id, character_id);
         CREATE UNIQUE INDEX pk_hrs_pilot_pair ON historic_relationship_summary(pilot_a_id, pilot_b_id);
         CREATE UNIQUE INDEX pk_hroc_pilot_pair ON historic_relationship_org_context(pilot_a_id, pilot_b_id);",
    )
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::env;
    use std::time::{SystemTime, UNIX_EPOCH};

    /// Section 6.4 Agent Test Independence: a unique, nanosecond-suffixed
    /// temp directory per test, never shared between tests and never
    /// dependent on execution order.
    fn unique_temp_root(test_name: &str) -> PathBuf {
        let suffix = SystemTime::now().duration_since(UNIX_EPOCH).unwrap().as_nanos();
        env::temp_dir().join(format!("killright-promotion-test-{test_name}-{suffix}"))
    }

    #[test]
    fn promoted_file_name_strips_core_prefix() {
        assert_eq!(
            promoted_file_name("CORE.KillRight.History.260810.01.duckdb").as_deref(),
            Some("KillRight.History.260810.01.duckdb")
        );
    }

    #[test]
    fn copy_to_live_copies_the_file_with_the_promoted_name() {
        let root = unique_temp_root("copy-to-live");
        let working_directory = folder_layout::working_dir(&root);
        fs::create_dir_all(&working_directory).unwrap();
        fs::write(working_directory.join("CORE.KillRight.History.260810.01.duckdb"), b"fixture working database")
            .unwrap();

        let live_path = copy_to_live(&root, "CORE.KillRight.History.260810.01.duckdb").unwrap();

        assert_eq!(live_path, folder_layout::live_dir(&root).join("KillRight.History.260810.01.duckdb"));
        assert!(live_path.exists());
        assert_eq!(fs::read(&live_path).unwrap(), b"fixture working database");

        fs::remove_dir_all(&root).unwrap();
    }

    #[test]
    fn move_to_failed_moves_the_file_and_returns_the_new_path() {
        let root = unique_temp_root("move-to-failed");
        let live_directory = folder_layout::live_dir(&root);
        fs::create_dir_all(&live_directory).unwrap();
        let live_path = live_directory.join("KillRight.History.260810.01.duckdb");
        fs::write(&live_path, b"fixture failed-validation database").unwrap();

        let failed_path = move_to_failed(&root, &live_path).unwrap();

        assert_eq!(failed_path, folder_layout::failed_dir(&root).join("KillRight.History.260810.01.duckdb"));
        assert!(!live_path.exists());
        assert!(failed_path.exists());
        assert_eq!(fs::read(&failed_path).unwrap(), b"fixture failed-validation database");

        fs::remove_dir_all(&root).unwrap();
    }

    #[test]
    fn add_promotion_constraints_creates_five_unique_indexes() {
        let connection = Connection::open_in_memory().unwrap();
        crate::schema::create_schema(&connection).unwrap();

        add_promotion_constraints(&connection).unwrap();

        let index_count: i64 = connection
            .query_row(
                "SELECT COUNT(*) FROM duckdb_indexes() WHERE index_name IN ( \
                     'pk_hre_evidence_id', 'uq_hre_source_killmail_id', \
                     'pk_hrep_evidence_character', 'pk_hrs_pilot_pair', 'pk_hroc_pilot_pair' \
                 );",
                [],
                |row| row.get(0),
            )
            .unwrap();

        assert_eq!(index_count, 5);
    }

    #[test]
    fn promoted_file_name_returns_none_when_prefix_is_missing() {
        assert_eq!(promoted_file_name("KillRight.History.260810.01.duckdb"), None);
    }

    #[test]
    fn copy_to_live_returns_error_when_working_file_name_lacks_core_prefix() {
        let root = unique_temp_root("copy-to-live-bad-prefix");

        let result = copy_to_live(&root, "KillRight.History.260810.01.duckdb");

        assert!(result.is_err());
    }

    #[test]
    fn add_promotion_constraints_fails_when_evidence_id_has_a_duplicate() {
        let connection = Connection::open_in_memory().unwrap();
        crate::schema::create_schema(&connection).unwrap();

        connection
            .execute_batch(
                "INSERT INTO historic_relationship_evidence \
                 (evidence_id, source_killmail_id, killmail_time_utc, evidence_date_utc, participant_count, created_utc) \
                 VALUES (1, 100, '2016-08-01T00:00:00Z', '2016-08-01', 2, '2016-08-01T00:00:00Z'); \
                 INSERT INTO historic_relationship_evidence \
                 (evidence_id, source_killmail_id, killmail_time_utc, evidence_date_utc, participant_count, created_utc) \
                 VALUES (1, 101, '2016-08-01T00:00:00Z', '2016-08-01', 2, '2016-08-01T00:00:00Z');",
            )
            .unwrap();

        let result = add_promotion_constraints(&connection);

        assert!(result.is_err());
    }
}
