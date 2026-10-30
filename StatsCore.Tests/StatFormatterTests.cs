using Xunit;

namespace StatsCore.Tests
{
    public class StatFormatterTests
    {
        [Theory]
        [InlineData(0, "0s")]
        [InlineData(45, "45s")]
        [InlineData(60, "1m 00s")]
        [InlineData(725, "12m 05s")]
        [InlineData(3600, "1h 00m")]
        [InlineData(11220, "3h 07m")]
        [InlineData(86400, "1d 0h")]
        [InlineData(190800, "2d 5h")]
        public void DurationReadsInTheLargestTwoUnits(long seconds, string expected) =>
            Assert.Equal(expected, StatFormatter.Duration(seconds));

        [Fact]
        public void NegativeDurationIsZero() => Assert.Equal("0s", StatFormatter.Duration(-5));

        [Theory]
        [InlineData(0, "0 m")]
        [InlineData(850, "850 m")]
        [InlineData(1000, "1.0 km")]
        [InlineData(12400, "12.4 km")]
        [InlineData(99999, "100.0 km")]
        [InlineData(340000, "340 km")]
        public void DistanceSwitchesToKilometersAtAThousand(long meters, string expected) =>
            Assert.Equal(expected, StatFormatter.Distance(meters));

        [Fact]
        public void CountsGetThousandsSeparators() =>
            Assert.Equal("1,234,567", StatFormatter.Value(1234567, StatFormat.Count));

        [Fact]
        public void RawIsAPlainNumberToo() =>
            Assert.Equal("42", StatFormatter.Value(42, StatFormat.Raw));

        [Fact]
        public void MoneyHasADollarSignAndKeepsTheMinusInFront() =>
            Assert.Equal("-$1,500", StatFormatter.Value(-1500, StatFormat.Money));

        [Fact]
        public void PercentGetsASymbol() =>
            Assert.Equal("45%", StatFormatter.Value(45, StatFormat.Percent));

        [Theory]
        [InlineData("vippy.pourdecisions.drinksTaken", "Drinks Taken")]
        [InlineData("statscore.deaths", "Deaths")]
        [InlineData("a.b.first_blood", "First Blood")]
        [InlineData("plainkey", "Plainkey")]
        public void FallbackNameComesFromTheLastKeySegment(string key, string expected) =>
            Assert.Equal(expected, StatFormatter.Fallback(key));

        [Theory]
        [InlineData("76561198032409969", true)]
        [InlineData("Apex Predator", false)]
        [InlineData("_team", false)]
        [InlineData("12345", false)]
        public void SteamIdsAreSeventeenDigits(string key, bool expected) =>
            Assert.Equal(expected, StatFormatter.LooksLikeSteamId(key));
    }
}
