namespace Killright.Integration.zKill.History;

public sealed record RelationshipFrequencyDistribution(
    long Once,
    long TwoToFive,
    long SixToTen,
    long ElevenToTwentyFive,
    long TwentySixToFifty,
    long FiftyOnePlus)
{
    public static RelationshipFrequencyDistribution FromCounts(IEnumerable<int> occurrenceCounts)
    {
        var once = 0L;
        var twoToFive = 0L;
        var sixToTen = 0L;
        var elevenToTwentyFive = 0L;
        var twentySixToFifty = 0L;
        var fiftyOnePlus = 0L;

        foreach (var count in occurrenceCounts)
        {
            if (count <= 1)
            {
                once++;
            }
            else if (count <= 5)
            {
                twoToFive++;
            }
            else if (count <= 10)
            {
                sixToTen++;
            }
            else if (count <= 25)
            {
                elevenToTwentyFive++;
            }
            else if (count <= 50)
            {
                twentySixToFifty++;
            }
            else
            {
                fiftyOnePlus++;
            }
        }

        return new RelationshipFrequencyDistribution(
            once,
            twoToFive,
            sixToTen,
            elevenToTwentyFive,
            twentySixToFifty,
            fiftyOnePlus);
    }
}