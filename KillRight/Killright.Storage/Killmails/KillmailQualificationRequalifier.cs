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
            using (var scope = database.BeginWrite())
            {
                using var command = scope.Connection.CreateCommand();
                command.Transaction = scope.Transaction;
                command.CommandText = $"""
                                      UPDATE main.zkill_killmails
                                      SET is_qualifying = 0
                                      WHERE is_qualifying = 1
                                        AND unique_attacker_count >= {configuredThreshold};
                                      """;
                command.ExecuteNonQuery();

                scope.Commit();
            }

            database.SetLastAppliedQualificationFleetThreshold(configuredThreshold);
            return;
        }

        if (configuredThreshold > storedThreshold.Value)
            database.SetLastAppliedQualificationFleetThreshold(configuredThreshold);
    }
}
