using System;
using System.Collections.Generic;
using CellularCore;
using Xunit;

namespace CellularUnitTests
{
    public class StatsCalculatorTests
    {
        [Fact]
        public void TestStrikePercentages()
        {
            int strikes = 30;
            int frames = 120;
            double result = StatsCalculator.CalculatePercentage(strikes, frames);
            Assert.Equal(0.25, result);
        }

        [Fact]
        public void TestSparePercentages()
        {
            int spares = 56;
            int frames = 120;
            int spares2 = 4;
            int frames2 = 64;
            double result = StatsCalculator.CalculatePercentage(spares, frames);
            double result2 = StatsCalculator.CalculatePercentage(spares2, frames2);
            Assert.Equal(56.0 / 120.0, result, 6);
            Assert.Equal(4.0 / 64.0, result2, 6);
        }

        [Fact]
        public void TestScoreAverage()
        {
            IEnumerable<int?> scores = new List<int?>() {145, 90, 212, 167, 98};
            IEnumerable<int?> scores2 = new List<int?>() {300, 76, 212, 167, 254};
            double result = StatsCalculator.CalculateAverage(scores);
            double result2 = StatsCalculator.CalculateAverage(scores2);
            Assert.Equal(712.0 / 5.0, result, 6);    // 142.4
            Assert.Equal(1009.0 / 5.0, result2, 6); // 201.8
        }

        // ---------- Additional tests ----------

        [Fact]
        public void CalculatePercentage_ZeroFrames_ReturnsZero()
        {
            double result = StatsCalculator.CalculatePercentage(5, 0);
            Assert.Equal(0.0, result);
        }

        [Fact]
        public void CalculatePercentage_Fractional_RoundsAsDouble()
        {
            double expected = 1.0 / 3.0;
            double result = StatsCalculator.CalculatePercentage(1, 3);
            Assert.Equal(expected, result, 6); // compare to 6 decimal places
        }

        [Fact]
        public void CalculatePercentage_NegativeValues_Handled()
        {
            double result = StatsCalculator.CalculatePercentage(-5, 10);
            Assert.Equal(-0.5, result);
        }

        [Fact]
        public void CalculateAverage_WithNulls_IgnoresNullsInSum()
        {
            IEnumerable<int?> scores = new List<int?>() { 300, null, 200 };
            double result = StatsCalculator.CalculateAverage(scores);
            Assert.Equal((300.0 + 0.0 + 200.0) / 3.0, result, 6); // 500/3
        }

        [Fact]
        public void CalculateAverage_AllNulls_ReturnsZeroAverage()
        {
            IEnumerable<int?> scores = new List<int?>() { null, null, null };
            double result = StatsCalculator.CalculateAverage(scores);
            Assert.Equal(0.0, result);
        }

        [Fact]
        public void CalculateAverage_EmptyList_ReturnsNaN()
        {
            IEnumerable<int?> scores = new List<int?>();
            double result = StatsCalculator.CalculateAverage(scores);
            Assert.True(double.IsNaN(result));
        }

        [Fact]
        public void CalculateAverage_SingleValue_ReturnsThatValue()
        {
            IEnumerable<int?> scores = new List<int?>() { 100 };
            double result = StatsCalculator.CalculateAverage(scores);
            Assert.Equal(100.0, result);
        }

        [Fact]
        public void CalculateAverage_WithNegativeValues_ComputesCorrectly()
        {
            IEnumerable<int?> scores = new List<int?>() { -10, 20, 30 };
            double result = StatsCalculator.CalculateAverage(scores);
            Assert.Equal(( -10.0 + 20.0 + 30.0 ) / 3.0, result, 6);
        }
    }
}