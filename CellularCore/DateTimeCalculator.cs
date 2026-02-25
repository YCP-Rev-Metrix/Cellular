using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CellularCore
{
    public static class DateTimeCalculator
    {
        public static DateTime CombineDateAndTime(DateOnly? date, TimeOnly? time)
        {
            if (date.HasValue && time.HasValue)
            {
                return date.Value.ToDateTime(time.Value);
            }

            if (date.HasValue)
            {
                // date at midnight
                return date.Value.ToDateTime(TimeOnly.MinValue);
            }

            if (time.HasValue)
            {
                // today's date with provided time
                return DateOnly.FromDateTime(DateTime.Now).ToDateTime(time.Value);
            }

            // fallback to now
            return DateTime.Now;
        }
    }
}
