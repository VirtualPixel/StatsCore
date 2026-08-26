using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace StatsCore.Tests
{
    public class CareerFileTests
    {
        private static Dictionary<string, Dictionary<string, CareerEntry>> Totals(
            string key, string slot, int value, long first = 10, long last = 20) =>
            new() { [key] = new Dictionary<string, CareerEntry> { [slot] = new CareerEntry { Value = value, FirstUtc = first, LastUtc = last } } };

        [Fact]
        public void TotalsRoundTrip()
        {
            string text = CareerFile.SerializeTotals(Totals("vippy.mod.drinks", "7656119", 42, 111, 222), new Dictionary<string, Dictionary<string, CareerEntry>>());
            var back = CareerFile.ParseTotals(text.Split('\n'));

            var entry = back["vippy.mod.drinks"]["7656119"];
            Assert.Equal(42, entry.Value);
            Assert.Equal(111, entry.FirstUtc);
            Assert.Equal(222, entry.LastUtc);
        }

        [Fact]
        public void HeaderIsWrittenAndIgnoredOnRead()
        {
            string text = CareerFile.SerializeTotals(Totals("a.b.c", "id", 1), new Dictionary<string, Dictionary<string, CareerEntry>>());
            Assert.StartsWith(CareerFile.TotalsHeader, text);
            Assert.Single(CareerFile.ParseTotals(text.Split('\n')));
        }

        [Fact]
        public void FilesWrittenBeforeVersioningStillLoad()
        {
            // No header, no timestamps: the shape 0.1.0 wrote.
            var back = CareerFile.ParseTotals(new[] { "statscore.deaths|7656119=9" });
            Assert.Equal(9, back["statscore.deaths"]["7656119"].Value);
            Assert.Equal(0, back["statscore.deaths"]["7656119"].FirstUtc);
        }

        [Fact]
        public void OneMangledLineCostsOneSlotNotTheFile()
        {
            var back = CareerFile.ParseTotals(new[]
            {
                CareerFile.TotalsHeader,
                "statscore.deaths|aaa=3",
                "this line is garbage",
                "statscore.deaths|bbb=not-a-number",
                "statscore.deaths|ccc=5",
            });
            Assert.Equal(3, back["statscore.deaths"]["aaa"].Value);
            Assert.Equal(5, back["statscore.deaths"]["ccc"].Value);
            Assert.False(back["statscore.deaths"].ContainsKey("bbb"));
        }

        // The regression: a save used to emit only the stats defined in the running session, so playing one
        // evening without the mod that owns a career stat erased its rows for good.
        [Fact]
        public void RowsForStatsNotDefinedThisSessionSurviveASave()
        {
            var saved = Totals("vippy.pourdecisions.drinks", "7656119", 340);
            var live = Totals("statscore.deaths", "7656119", 12);

            var back = CareerFile.ParseTotals(CareerFile.SerializeTotals(live, saved).Split('\n'));

            Assert.Equal(340, back["vippy.pourdecisions.drinks"]["7656119"].Value);
            Assert.Equal(12, back["statscore.deaths"]["7656119"].Value);
        }

        [Fact]
        public void LiveValueWinsOverTheCopyReadFromDisk()
        {
            var saved = Totals("statscore.deaths", "7656119", 12);
            var live = Totals("statscore.deaths", "7656119", 19);

            var back = CareerFile.ParseTotals(CareerFile.SerializeTotals(live, saved).Split('\n'));

            Assert.Equal(19, back["statscore.deaths"]["7656119"].Value);
            Assert.Single(back["statscore.deaths"]);
        }

        [Fact]
        public void DailyBucketsForAStatThatNeverOptedInSurvive()
        {
            var saved = new Dictionary<string, Dictionary<string, Dictionary<int, int>>>
            {
                ["vippy.pourdecisions.drinks"] = new() { ["7656119"] = new Dictionary<int, int> { [20300] = 4, [20301] = 7 } },
            };
            var live = new Dictionary<string, Dictionary<string, Dictionary<int, int>>>();

            var back = CareerFile.ParseDaily(CareerFile.SerializeDaily(live, saved).Split('\n'));

            Assert.Equal(4, back["vippy.pourdecisions.drinks"]["7656119"][20300]);
            Assert.Equal(7, back["vippy.pourdecisions.drinks"]["7656119"][20301]);
        }

        [Fact]
        public void DailyRoundTrip()
        {
            var live = new Dictionary<string, Dictionary<string, Dictionary<int, int>>>
            {
                ["statscore.deaths"] = new() { ["7656119"] = new Dictionary<int, int> { [20000] = 1, [20005] = 3 } },
            };
            var back = CareerFile.ParseDaily(CareerFile.SerializeDaily(live, new Dictionary<string, Dictionary<string, Dictionary<int, int>>>()).Split('\n'));
            Assert.Equal(new[] { 1, 3 }, back["statscore.deaths"]["7656119"].OrderBy(kv => kv.Key).Select(kv => kv.Value));
        }

        [Fact]
        public void EmptyDayMapWritesNoLine()
        {
            var live = new Dictionary<string, Dictionary<string, Dictionary<int, int>>>
            {
                ["statscore.deaths"] = new() { ["7656119"] = new Dictionary<int, int>() },
            };
            Assert.Empty(CareerFile.ParseDaily(CareerFile.SerializeDaily(live, new Dictionary<string, Dictionary<string, Dictionary<int, int>>>()).Split('\n')));
        }

        // The regression: slots keyed by a display name (enemy, level) are not steamIDs, and a name holding
        // a framing character used to split the line in the wrong place and come back as a different slot.
        [Theory]
        [InlineData("Slow=Mouth")]
        [InlineData("Head|Man")]
        [InlineData("Trudge;Bowtie")]
        [InlineData("Apex,Predator")]
        [InlineData("Manor: East Wing")]
        public void ASlotNameHoldingAFramingCharacterStillRoundTrips(string slot)
        {
            const string key = "statscore.damageTakenByEnemy";
            string safe = CareerFile.SanitizeSlot(slot);

            // The same slot has to survive both files. career-daily.txt frames on two bars, so a name with a
            // bar in it used to come back cut short there even when career.txt happened to parse it back.
            Assert.DoesNotContain('|', safe);
            Assert.DoesNotContain('=', safe);
            Assert.DoesNotContain(';', safe);
            Assert.DoesNotContain(',', safe);

            var totals = CareerFile.ParseTotals(CareerFile.SerializeTotals(Totals(key, slot, 77), new Dictionary<string, Dictionary<string, CareerEntry>>()).Split('\n'));
            Assert.Equal(77, totals[key][safe].Value);
            Assert.Single(totals[key]);

            var live = new Dictionary<string, Dictionary<string, Dictionary<int, int>>>
            {
                [key] = new() { [slot] = new Dictionary<int, int> { [20300] = 77 } },
            };
            var daily = CareerFile.ParseDaily(CareerFile.SerializeDaily(live, new Dictionary<string, Dictionary<string, Dictionary<int, int>>>()).Split('\n'));
            Assert.Equal(77, daily[key][safe][20300]);
        }

        [Fact]
        public void SanitizingIsIdempotentAndLeavesOrdinaryNamesAlone()
        {
            Assert.Equal("Huntsman", CareerFile.SanitizeSlot("Huntsman"));
            Assert.Equal("7656119", CareerFile.SanitizeSlot("7656119"));
            Assert.Equal("Slow_Mouth", CareerFile.SanitizeSlot("Slow=Mouth"));
            Assert.Equal(CareerFile.SanitizeSlot("Slow=Mouth"), CareerFile.SanitizeSlot(CareerFile.SanitizeSlot("Slow=Mouth")));
        }
    }
}
