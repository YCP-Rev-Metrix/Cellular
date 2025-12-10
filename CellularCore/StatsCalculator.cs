using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CellularCore
{
    public static class StatsCalculator
    {
        public static double CalculatePercentage(int num_stat, int num_frames)
        {
           return (double)num_stat/ (double)num_frames;
        }
    }
}
