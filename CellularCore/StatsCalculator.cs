using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CellularCore
{
    public static class StatsCalculator
    {

        // existing helper kept for other stats calculations
        public static double CalculatePercentage(int num_stat, int num_frames)
        {
           return num_frames == 0 ? 0.0 : (double)num_stat / (double)num_frames;
        }

        public static double CalculateAverage(IEnumerable<int?> scores)
        {
            double average = (double)scores.Sum() / (double)scores.Count();
            return average;
        }
    }
}
