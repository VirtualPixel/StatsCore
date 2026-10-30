using System.Collections.Generic;
using Xunit;

namespace StatsCore.Tests
{
    public class CareerMergeTests
    {
        private static Dictionary<string, Dictionary<string, CareerEntry>> Totals(string key, string slot, int value, long first, long last) =>
            new() { [key] = new() { [slot] = new CareerEntry { Value = value, FirstUtc = first, LastUtc = last } } };

        [Fact]
        public void MergedValuesAreAddedAndTheTimeSpanWidens()
        {
            var target = Totals("k", "me", 10, 200, 300);
            CareerFile.MergeTotals(target, Totals("k", "me", 5, 100, 250));
            var entry = target["k"]["me"];
            Assert.Equal(15, entry.Value);
            Assert.Equal(100, entry.FirstUtc);
            Assert.Equal(300, entry.LastUtc);
        }

        [Fact]
        public void AnUnknownFirstTimeNeverBeatsAKnownOne()
        {
            var target = Totals("k", "me", 1, 0, 0);
            CareerFile.MergeTotals(target, Totals("k", "me", 1, 500, 600));
            Assert.Equal(500, target["k"]["me"].FirstUtc);
        }

        [Fact]
        public void SlotsAndStatsOnlyInTheSourceAreCopiedIn()
        {
            var target = Totals("k", "me", 1, 10, 10);
            CareerFile.MergeTotals(target, Totals("other", "someone", 7, 20, 30));
            Assert.Equal(7, target["other"]["someone"].Value);
            Assert.Equal(1, target["k"]["me"].Value);
        }

        [Fact]
        public void MergingNeverOverflowsAnInt()
        {
            var target = Totals("k", "me", int.MaxValue - 1, 1, 1);
            CareerFile.MergeTotals(target, Totals("k", "me", 50, 1, 1));
            Assert.Equal(int.MaxValue, target["k"]["me"].Value);
        }

        [Fact]
        public void DailyBucketsAreAddedPerDay()
        {
            var target = new Dictionary<string, Dictionary<string, Dictionary<int, int>>> { ["k"] = new() { ["me"] = new() { [1] = 4, [2] = 1 } } };
            var source = new Dictionary<string, Dictionary<string, Dictionary<int, int>>> { ["k"] = new() { ["me"] = new() { [2] = 9, [3] = 2 } } };
            CareerFile.MergeDaily(target, source);
            Assert.Equal(4, target["k"]["me"][1]);
            Assert.Equal(10, target["k"]["me"][2]);
            Assert.Equal(2, target["k"]["me"][3]);
        }

        [Fact]
        public void MergedMarkersSurviveAWriteAndRead()
        {
            var empty = new Dictionary<string, Dictionary<string, CareerEntry>>();
            string text = CareerFile.SerializeTotals(empty, empty, new[] { "abc123", "def456" });
            var found = CareerFile.ParseMerged(text.Split('\n'));
            Assert.Contains("abc123", found);
            Assert.Contains("def456", found);
            Assert.Empty(CareerFile.ParseTotals(text.Split('\n')));
        }

        [Fact]
        public void TotalsWrittenWithMarkersStillReadBack()
        {
            var live = Totals("k", "me", 12, 5, 6);
            string text = CareerFile.SerializeTotals(live, new Dictionary<string, Dictionary<string, CareerEntry>>(), new[] { "x" });
            Assert.Equal(12, CareerFile.ParseTotals(text.Split('\n'))["k"]["me"].Value);
        }
    }
}
