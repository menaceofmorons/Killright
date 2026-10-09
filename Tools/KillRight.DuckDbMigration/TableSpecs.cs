namespace KillRight.DuckDbMigration;

public enum ColumnKind
{
    Integer,
    Real,
    Text,
    Flag,
    Time
}

public sealed record ColumnSpec(string Name, ColumnKind Kind);

public sealed record TableSpec(string Name, IReadOnlyList<ColumnSpec> Columns, bool SkipOrphanAttackers = false);

public static class TableSpecs
{
    public static readonly IReadOnlyList<TableSpec> All =
    [
        new("zkill_killmails",
        [
            new("killmail_id", ColumnKind.Integer),
            new("killmail_hash", ColumnKind.Text),
            new("kill_time_utc", ColumnKind.Time),
            new("system_id", ColumnKind.Integer),
            new("location_id", ColumnKind.Integer),
            new("victim_character_id", ColumnKind.Integer),
            new("victim_ship_type_id", ColumnKind.Integer),
            new("unique_attacker_count", ColumnKind.Integer),
            new("is_solo", ColumnKind.Flag),
            new("is_npc", ColumnKind.Flag),
            new("is_qualifying", ColumnKind.Flag),
            new("cached_at_utc", ColumnKind.Time)
        ]),
        new("zkill_killmail_attackers",
        [
            new("killmail_id", ColumnKind.Integer),
            new("character_id", ColumnKind.Integer),
            new("corporation_id", ColumnKind.Integer),
            new("alliance_id", ColumnKind.Integer),
            new("ship_type_id", ColumnKind.Integer),
            new("weapon_type_id", ColumnKind.Integer)
        ],
        SkipOrphanAttackers: true),
        new("zkill_activity_cache",
        [
            new("character_id", ColumnKind.Integer),
            new("has_public_activity_data", ColumnKind.Flag),
            new("kills_week", ColumnKind.Integer),
            new("solo_week", ColumnKind.Integer),
            new("last_active_utc", ColumnKind.Time),
            new("last_activity_type", ColumnKind.Text),
            new("checked_at_utc", ColumnKind.Time),
            new("error", ColumnKind.Text),
            new("last_recent_call_utc", ColumnKind.Time),
            new("recent_coverage_start_utc", ColumnKind.Time),
            new("last_kill_utc", ColumnKind.Time)
        ]),
        new("pilot_identity_cache",
        [
            new("input_name", ColumnKind.Text),
            new("character_id", ColumnKind.Integer),
            new("character_name", ColumnKind.Text),
            new("verify_status", ColumnKind.Text),
            new("security_status", ColumnKind.Real),
            new("corporation_id", ColumnKind.Integer),
            new("corporation_name", ColumnKind.Text),
            new("corporation_ticker", ColumnKind.Text),
            new("alliance_id", ColumnKind.Integer),
            new("alliance_name", ColumnKind.Text),
            new("alliance_ticker", ColumnKind.Text),
            new("cached_at_utc", ColumnKind.Time),
            new("birthday", ColumnKind.Time),
            new("security_status_at_utc", ColumnKind.Time),
            new("faction_id", ColumnKind.Integer)
        ]),
        new("esi_entity_name_cache",
        [
            new("entity_id", ColumnKind.Integer),
            new("entity_type", ColumnKind.Text),
            new("name", ColumnKind.Text)
        ]),
        new("zkill_statistics_cache",
        [
            new("character_id", ColumnKind.Integer),
            new("ships_destroyed", ColumnKind.Integer),
            new("solo_kills", ColumnKind.Integer),
            new("solo_ratio", ColumnKind.Real),
            new("avg_gang_size", ColumnKind.Real),
            new("ships_lost", ColumnKind.Integer),
            new("solo_losses", ColumnKind.Integer),
            new("general_style", ColumnKind.Text),
            new("checked_at_utc", ColumnKind.Time),
            new("months_processed", ColumnKind.Flag),
            new("no_history_marker", ColumnKind.Flag),
            new("pod_kills", ColumnKind.Integer),
            new("pod_losses", ColumnKind.Integer)
        ]),
        new("pilot_last_killmail_cache",
        [
            new("character_id", ColumnKind.Integer),
            new("has_killmail", ColumnKind.Flag),
            new("killmail_id", ColumnKind.Integer),
            new("kill_time_utc", ColumnKind.Time),
            new("activity_type", ColumnKind.Text),
            new("system_id", ColumnKind.Integer),
            new("ship_type_id", ColumnKind.Integer),
            new("victim_ship_type_id", ColumnKind.Integer),
            new("attacker_count", ColumnKind.Integer),
            new("weapon_type_id", ColumnKind.Integer),
            new("checked_at_utc", ColumnKind.Time)
        ])
    ];
}
