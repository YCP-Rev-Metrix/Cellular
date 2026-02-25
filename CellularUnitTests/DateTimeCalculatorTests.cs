using System;
using Xunit;
using CellularCore;

namespace CellularUnitTests
{
    public class DateTimeCalculatorTests
    {
        [Fact]
        public void CombineDateAndTime_BothDateAndTime_ReturnsCombinedDateTime()
        {
            var date = new DateOnly(2001, 2, 3);
            var time = new TimeOnly(13, 14, 15);

            var expected = date.ToDateTime(time);
            var actual = DateTimeCalculator.CombineDateAndTime(date, time);

            Assert.Equal(expected, actual);
        }

        [Fact]
        public void CombineDateAndTime_DateOnly_ReturnsDateAtMidnight()
        {
            var date = new DateOnly(2022, 5, 10);

            var expected = date.ToDateTime(TimeOnly.MinValue);
            var actual = DateTimeCalculator.CombineDateAndTime(date, null);

            Assert.Equal(expected, actual);
        }

        [Fact]
        public void CombineDateAndTime_TimeOnly_ReturnsTodayWithGivenTime()
        {
            var time = new TimeOnly(9, 8, 7);
            var todayBefore = DateOnly.FromDateTime(DateTime.Now);

            var actual = DateTimeCalculator.CombineDateAndTime(null, time);

            var actualDate = DateOnly.FromDateTime(actual);
            var actualTime = TimeOnly.FromTimeSpan(actual.TimeOfDay);

            Assert.Equal(todayBefore, actualDate);
            Assert.Equal(time, actualTime);
        }

        [Fact]
        public void CombineDateAndTime_BothNull_ReturnsRecentNow()
        {
            var before = DateTime.Now;
            var result = DateTimeCalculator.CombineDateAndTime(null, null);
            var after = DateTime.Now;

            // result should be captured between before and after (allow 1s leeway)
            Assert.InRange(result, before.AddSeconds(-1), after.AddSeconds(1));
        }
    }
}