using System.Globalization;
using System.Text;

namespace Killright.Integration.zKill.History;

public sealed class ZkillHistoryPeriodResult
{
    public required DateOnly StartDate { get; init; }
    public required DateOnly EndDate { get; init; }
    public required TimeSpan Elapsed { get; init; }
    public required IReadOnlyList<ZkillHistoryDayResult> Days { get; init; }
    public required int UniqueQualifyingPilots { get; init; }
    public required long UniqueCandidateRelationships { get; init; }
    public required RelationshipFrequencyDistribution RelationshipFrequencyDistribution { get; init; }

    public int RawKillmails => Days.Sum(x => x.RawKillmailCount);

    public int RawAttackers => Days.Sum(x => x.RawAttackerCount);

    public int PodKillmails => Days.Sum(x => x.PodKillmailCount);

    public int InsufficientAttackerKillmails => Days.Sum(x => x.InsufficientAttackerKillmailCount);

    public int FleetKillmails => Days.Sum(x => x.FleetKillmailCount);

    public int QualifyingKillmails => Days.Sum(x => x.QualifyingKillmailCount);

    public int QualifyingAttackers => Days.Sum(x => x.QualifyingAttackerCount);

    public long CandidateEventEvidenceRows => QualifyingKillmails;

    public long CandidateParticipantIndexRows => QualifyingAttackers;

    public long CandidatePairOccurrenceRows => Days.Sum(x => x.CandidatePairOccurrenceRows);

    public int SuccessfulDays => Days.Count(x => x.Succeeded);

    public int FailedDays => Days.Count(x => !x.Succeeded);

    public double AverageRawKillmailsPerDay => Days.Count == 0 ? 0 : RawKillmails / (double)Days.Count;

    public double AverageRawAttackersPerKillmail => RawKillmails == 0 ? 0 : RawAttackers / (double)RawKillmails;

    public double AverageQualifyingAttackersPerKillmail => QualifyingKillmails == 0 ? 0 : QualifyingAttackers / (double)QualifyingKillmails;

    public int HighestQualifyingAttackersOnSingleKillmail => Days.Count == 0 ? 0 : Days.Max(x => x.MaxQualifyingAttackersOnKillmail);

    public string PeriodLabel => $"{StartDate:yyyy-MM-dd} to {EndDate:yyyy-MM-dd}";

    public ZkillHistoryDayResult? HighestRawKillmailDay => Days.Count == 0 ? null : Days.OrderByDescending(x => x.RawKillmailCount).First();

    public ZkillHistoryDayResult? LowestRawKillmailDay => Days.Count == 0 ? null : Days.Where(x => x.Succeeded).OrderBy(x => x.RawKillmailCount).FirstOrDefault();

    public ZkillHistoryDayResult? HighestQualifyingKillmailDay => Days.Count == 0 ? null : Days.OrderByDescending(x => x.QualifyingKillmailCount).First();

    public string ToShareableReport()
    {
        var builder = new StringBuilder();
        var culture = CultureInfo.InvariantCulture;

        builder.AppendLine("====================================================");
        builder.AppendLine("KillRight Group Detection Winter Nexus Quarter Pilot");
        builder.AppendLine("====================================================");
        builder.AppendLine();
        builder.AppendLine($"Period: {PeriodLabel}");
        builder.AppendLine($"Days processed: {Days.Count.ToString("N0", culture)}");
        builder.AppendLine($"Successful days: {SuccessfulDays.ToString("N0", culture)}");
        builder.AppendLine($"Failed days: {FailedDays.ToString("N0", culture)}");
        builder.AppendLine();

        AppendPeriodTotals(builder, culture);
        AppendMonthlyBreakdown(builder, culture);
        AppendRelationshipDistribution(builder, culture);

        builder.AppendLine($"Elapsed time: {Elapsed:hh\\:mm\\:ss\\.fff}");

        if (FailedDays > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Failed days:");

            foreach (var failedDay in Days.Where(x => !x.Succeeded))
                builder.AppendLine($"- {failedDay.Date:yyyy-MM-dd}: {failedDay.ErrorMessage}");
        }

        builder.AppendLine();
        builder.AppendLine("Notes:");
        builder.AppendLine("- Historic measurement does not exclude same-corporation or same-alliance attackers.");
        builder.AppendLine("- Candidate rows are measurement-only and are not written to DuckDB.");
        builder.AppendLine("- Candidate compressed relationship rows are unique unordered pilot pairs across the full period.");
        builder.AppendLine("- Relationship frequency distribution counts qualifying pair appearances across the full period.");
        builder.AppendLine();
        builder.AppendLine("====================================================");

        return builder.ToString();
    }

    private void AppendPeriodTotals(StringBuilder builder, CultureInfo culture)
    {
        builder.AppendLine("Period totals");
        builder.AppendLine($"Raw killmails: {RawKillmails.ToString("N0", culture)}");
        builder.AppendLine($"Raw attackers: {RawAttackers.ToString("N0", culture)}");
        builder.AppendLine($"Average raw killmails per day: {AverageRawKillmailsPerDay.ToString("N2", culture)}");
        builder.AppendLine($"Average raw attackers per killmail: {AverageRawAttackersPerKillmail.ToString("N2", culture)}");

        if (HighestRawKillmailDay is not null)
            builder.AppendLine($"Highest raw day: {HighestRawKillmailDay.Date:yyyy-MM-dd} = {HighestRawKillmailDay.RawKillmailCount.ToString("N0", culture)}");

        if (LowestRawKillmailDay is not null)
            builder.AppendLine($"Lowest raw day: {LowestRawKillmailDay.Date:yyyy-MM-dd} = {LowestRawKillmailDay.RawKillmailCount.ToString("N0", culture)}");

        builder.AppendLine();
        builder.AppendLine("Qualification filters");
        builder.AppendLine($"Pod killmails excluded: {PodKillmails.ToString("N0", culture)}");
        builder.AppendLine($"Solo or insufficient attacker killmails excluded: {InsufficientAttackerKillmails.ToString("N0", culture)}");
        builder.AppendLine($"Fleet killmails excluded: {FleetKillmails.ToString("N0", culture)}");
        builder.AppendLine($"Qualifying killmails: {QualifyingKillmails.ToString("N0", culture)}");
        builder.AppendLine($"Qualifying attackers: {QualifyingAttackers.ToString("N0", culture)}");
        builder.AppendLine($"Average qualifying attackers per killmail: {AverageQualifyingAttackersPerKillmail.ToString("N2", culture)}");
        builder.AppendLine($"Highest qualifying attackers on single killmail: {HighestQualifyingAttackersOnSingleKillmail.ToString("N0", culture)}");

        if (HighestQualifyingKillmailDay is not null)
            builder.AppendLine($"Highest qualifying day: {HighestQualifyingKillmailDay.Date:yyyy-MM-dd} = {HighestQualifyingKillmailDay.QualifyingKillmailCount.ToString("N0", culture)}");

        builder.AppendLine();
        builder.AppendLine("Candidate storage row counts");
        builder.AppendLine($"Candidate event evidence rows: {CandidateEventEvidenceRows.ToString("N0", culture)}");
        builder.AppendLine($"Candidate participant index rows: {CandidateParticipantIndexRows.ToString("N0", culture)}");
        builder.AppendLine($"Candidate pair occurrence rows: {CandidatePairOccurrenceRows.ToString("N0", culture)}");
        builder.AppendLine($"Candidate compressed relationship rows: {UniqueCandidateRelationships.ToString("N0", culture)}");
        builder.AppendLine($"Unique qualifying pilots: {UniqueQualifyingPilots.ToString("N0", culture)}");
        builder.AppendLine();
    }

    private void AppendMonthlyBreakdown(StringBuilder builder, CultureInfo culture)
    {
        builder.AppendLine("Monthly breakdown");

        foreach (var monthGroup in Days.GroupBy(x => new DateOnly(x.Date.Year, x.Date.Month, 1)).OrderBy(x => x.Key))
        {
            var days = monthGroup.ToList();
            var rawKillmails = days.Sum(x => x.RawKillmailCount);
            var qualifyingKillmails = days.Sum(x => x.QualifyingKillmailCount);
            var pairRows = days.Sum(x => x.CandidatePairOccurrenceRows);

            builder.AppendLine($"{monthGroup.Key:yyyy-MM}: raw={rawKillmails.ToString("N0", culture)}, qualifying={qualifyingKillmails.ToString("N0", culture)}, pair_occurrences={pairRows.ToString("N0", culture)}");
        }

        builder.AppendLine();
    }

    private void AppendRelationshipDistribution(StringBuilder builder, CultureInfo culture)
    {
        builder.AppendLine("Relationship frequency distribution");
        builder.AppendLine($"1 appearance: {RelationshipFrequencyDistribution.Once.ToString("N0", culture)}");
        builder.AppendLine($"2-5 appearances: {RelationshipFrequencyDistribution.TwoToFive.ToString("N0", culture)}");
        builder.AppendLine($"6-10 appearances: {RelationshipFrequencyDistribution.SixToTen.ToString("N0", culture)}");
        builder.AppendLine($"11-25 appearances: {RelationshipFrequencyDistribution.ElevenToTwentyFive.ToString("N0", culture)}");
        builder.AppendLine($"26-50 appearances: {RelationshipFrequencyDistribution.TwentySixToFifty.ToString("N0", culture)}");
        builder.AppendLine($"51+ appearances: {RelationshipFrequencyDistribution.FiftyOnePlus.ToString("N0", culture)}");
        builder.AppendLine();
    }
}