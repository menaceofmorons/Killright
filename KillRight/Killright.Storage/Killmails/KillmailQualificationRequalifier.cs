using DuckDB.NET.Data;
using Killright.Storage.Database;

namespace Killright.Storage.Killmails;

public static class KillmailQualificationRequalifier
{
    public static void RequalifyOnStartup(KillRightDatabase database, int configuredThreshold)
    {
        var storedThreshold = database.GetLastAppliedQualificationFleetThreshold();

        if (storedThreshold is null)
        {
            database.SetLastAppliedQualificationFleetThreshold(configuredThreshold);
            return;
        }

        if (configuredThreshold < storedThreshold.Value)
        {
            using var connection = new DuckDBConnection(database.ConnectionString);
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = $"""
                                  UPDATE main.zkill_killmails
                                  SET is_qualifying = FALSE
                                  WHERE is_qualifying = TRUE
                                    AND unique_attacker_count >= {configuredThreshold};
                                  """;
            command.ExecuteNonQuery();

            database.SetLastAppliedQualificationFleetThreshold(configuredThreshold);
            return;
        }

        if (configuredThreshold > storedThreshold.Value)
            database.SetLastAppliedQualificationFleetThreshold(configuredThreshold);
    }
}
