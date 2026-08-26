using System;
using Xunit;

namespace StatsCore.Tests
{
    public class DayMathTests
    {
        // The regression: buckets were indexed by UTC day. For anyone west of Greenwich that rolls the day
        // over during the evening, which is when REPO gets played, so "today" was wrong for hours every night.
        [Fact]
        public void TodayIsTheLocalCalendarDay()
        {
            Assert.Equal(DayMath.IndexOf(DateTime.Today), DayMath.Today());
        }

        [Fact]
        public void EveningLocalTimeIsStillTodayWhenUtcHasAlreadyRolledOver()
        {
            // 22:00 local. In UTC-5 that is 03:00 the next day; the UTC bucket would be tomorrow's.
            var evening = new DateTime(2026, 9, 4, 22, 0, 0, DateTimeKind.Local);
            Assert.Equal(DayMath.IndexOf(new DateTime(2026, 9, 4, 9, 0, 0, DateTimeKind.Local)), DayMath.IndexOf(evening));
        }

        [Fact]
        public void TimeOfDayIsIgnored()
        {
            var early = new DateTime(2026, 3, 1, 0, 0, 1, DateTimeKind.Local);
            var late = new DateTime(2026, 3, 1, 23, 59, 59, DateTimeKind.Local);
            Assert.Equal(DayMath.IndexOf(early), DayMath.IndexOf(late));
        }

        [Fact]
        public void AUtcValueIsConvertedBeforeTheDateIsRead()
        {
            var utc = new DateTime(2026, 9, 5, 2, 0, 0, DateTimeKind.Utc);
            Assert.Equal(DayMath.IndexOf(utc.ToLocalTime()), DayMath.IndexOf(utc));
        }

        [Fact]
        public void ConsecutiveDaysAreConsecutiveIndices()
        {
            var day = new DateTime(2026, 12, 31, 12, 0, 0, DateTimeKind.Local);
            Assert.Equal(DayMath.IndexOf(day) + 1, DayMath.IndexOf(day.AddDays(1)));
        }

        // A DST boundary shifts wall-clock time by an hour. Reading only the date part means the index still
        // moves by exactly one, where dividing an epoch second count by 86400 would not.
        [Fact]
        public void DstBoundariesDoNotSkipOrRepeatADay()
        {
            var before = new DateTime(2026, 3, 7, 12, 0, 0, DateTimeKind.Local);
            for (int i = 0; i < 5; i++)
                Assert.Equal(DayMath.IndexOf(before) + i, DayMath.IndexOf(before.AddDays(i)));
        }

        [Fact]
        public void IndexAndDateAreInverses()
        {
            var day = new DateTime(2026, 9, 4, 0, 0, 0, DateTimeKind.Local);
            Assert.Equal(day.Date, DayMath.DateOf(DayMath.IndexOf(day)));
        }

        [Fact]
        public void EpochIsDayZero()
        {
            Assert.Equal(0, DayMath.IndexOf(new DateTime(1970, 1, 1, 6, 0, 0, DateTimeKind.Local)));
        }
    }
}
