using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SQLite;

namespace Cellular.ViewModel
{
    [Table("shot")]
    public class Shot
    {
        [PrimaryKey, AutoIncrement]
        public int ShotId { get; set; }
        public int? ShotNumber { get; set; }
        public int? Ball { get; set; }
        public int? Count { get; set; }
        public short? LeaveType { get; set; }
        public string? Side { get; set; }
        public string? Position { get; set; }
        public int? Frame { get; set; }
        public string? Comment { get; set; }
        public string? Speed { get; set; }
        public int Stance { get; set; }

        /// <summary>Server-assigned row id from the cloud API. Used to correlate local rows across devices.</summary>
        public int? CloudID { get; set; }
    }
}
