using System.Globalization;
using Killright.UI.ViewModels;
using Xunit;

namespace Killright.UI.Tests.ViewModels;

public sealed class SecurityStatusFormatterTests
{
    [Theory]
    [InlineData(-2.37, "-2.3")]
    [InlineData(-2.3, "-2.3")]
    [InlineData(4.99, "4.9")]
    [InlineData(5.0, "5.0")]
    [InlineData(-0.04, "0.0")]
    [InlineData(0.0, "0.0")]
    [InlineData(-10.0, "-10.0")]
    [InlineData(-5.04, "-5.0")]
    public void Format_TruncatesTowardZeroToOneDecimal(double value, string expected)
    {
        Assert.Equal(expected, SecurityStatusFormatter.Format(value));
    }

    [Fact]
    public void Format_Null_ReturnsUnk()
    {
        Assert.Equal("Unk", SecurityStatusFormatter.Format(null));
    }

    [Fact]
    public void Format_NonFinite_ReturnsUnk()
    {
        Assert.Equal("Unk", SecurityStatusFormatter.Format(double.NaN));
        Assert.Equal("Unk", SecurityStatusFormatter.Format(double.PositiveInfinity));
    }

    [Fact]
    public void Format_CommaDecimalCulture_OutputIdentical()
    {
        var previous = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            Assert.Equal("-2.3", SecurityStatusFormatter.Format(-2.37));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
