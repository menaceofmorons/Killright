using System.Globalization;
using System.Text;

namespace Killright.Integration.zKill.History;

public sealed class ZkillHistoryMonthResult
{
    public required DateOnly StartDate { get; init; }
    public required DateOnly EndDate { get; init; }
    public required TimeSpan Elapsed { get; init; }
    public required IReadOnlyList<ZkillHistoryDayResult> Days { get; init; }

    public int TotalKillmails => Days.Sum(x => x.KillmailCount);

    public int TotalAttackers => Days.Sum(x => x.AttackerCount);

    public int SuccessfulDays => Days.Count(x => x.Succeeded);

    public int FailedDays => Days.Count(x => !x.Succeeded);

    public double AverageKillmailsPerDay => Days.Count == 0 ? 0 : TotalKillmails / (double)Days.Count;

    public double AverageAttackersPerKillmail => TotalKillmails == 0 ? 0 : TotalAttackers / (double)TotalKillmails;

    public int HighestAttackersOnSingleKillmail => Days.Count == 0 ? 0 : Days.Max(x => x.MaxAttackersOnKillmail);

    public string MonthLabel => $"{StartDate:yyyy-MM-dd} to {EndDate:yyyy-MM-dd}";

    public ZkillHistoryDayResult? HighestKillmailDay => Days.Count == 0 ? null : Days.OrderByDescending(x => x.KillmailCount).First();

    public ZkillHistoryDayResult? LowestKillmailDay => Days.Count == 0 ? null : Days.Where(x => x.Succeeded).OrderBy(x => x.KillmailCount).FirstOrDefault();

    public string ToShareableReport()
    {
        var builder = new StringBuilder();
        var culture = CultureInfo.InvariantCulture;

        builder.AppendLine("====================================================");
        builder.AppendLine("KillRight Group Detection History Pilot");
        builder.AppendLine("====================================================");
        builder.AppendLine();
        builder.AppendLine($"Month: {MonthLabel}");
        builder.AppendLine($"Days processed: {Days.Count.ToString("N0", culture)}");
        builder.AppendLine($"Successful days: {SuccessfulDays.ToString("N0", culture)}");
        builder.AppendLine($"Failed days: {FailedDays.ToString("N0", culture)}");
        builder.AppendLine();
        builder.AppendLine($"Total killmails: {TotalKillmails.ToString("N0", culture)}");
        builder.AppendLine($"Average killmails per day: {AverageKillmailsPerDay.ToString("N2", culture)}");

        if (HighestKillmailDay is not null)
            builder.AppendLine($"Highest day: {HighestKillmailDay.Date:yyyy-MM-dd} = {HighestKillmailDay.KillmailCount.ToString("N0", culture)}");

        if (LowestKillmailDay is not null)
            builder.AppendLine($"Lowest day: {LowestKillmailDay.Date:yyyy-MM-dd} = {LowestKillmailDay.KillmailCount.ToString("N0", culture)}");

        builder.AppendLine();
        builder.AppendLine($"Total attackers: {TotalAttackers.ToString("N0", culture)}");
        builder.AppendLine($"Average attackers per killmail: {AverageAttackersPerKillmail.ToString("N2", culture)}");
        builder.AppendLine($"Highest attackers on single killmail: {HighestAttackersOnSingleKillmail.ToString("N0", culture)}");
        builder.AppendLine();
        builder.AppendLine($"Elapsed time: {Elapsed:hh\\:mm\\:ss\\.fff}");

        if (FailedDays > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Failed days:");

            foreach (var failedDay in Days.Where(x => !x.Succeeded))
                builder.AppendLine($"- {failedDay.Date:yyyy-MM-dd}: {failedDay.ErrorMessage}");
        }

        builder.AppendLine();
        builder.AppendLine("====================================================");

        return builder.ToString();
    }
}