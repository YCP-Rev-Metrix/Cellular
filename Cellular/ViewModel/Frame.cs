using SQLite;

namespace Cellular.ViewModel
{
    [Table("bowlingFrame")]
    public class BowlingFrame
    {
        [PrimaryKey, AutoIncrement]
        public int FrameId { get; set; }
        public int UserId { get; set; }
        public int? FrameNumber { get; set; }
        public int? Lane { get; set; }
        public int? Result { get; set; }
        public string? Shots { get; set; }
    }
}
