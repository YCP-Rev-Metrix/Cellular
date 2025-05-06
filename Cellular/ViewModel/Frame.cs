using SQLite;

namespace Cellular.ViewModel
{
    [Table("bowlingFrame")]
    public class BowlingFrame
    {
        [PrimaryKey, AutoIncrement]
        public int FrameId { get; set; }
        public int? FrameNumber { get; set; }
        public int? Lane { get; set; }
        public string? Result { get; set; }
        public int? GameId { get; set; }
        public int? Shot1 { get; set; }
        public int? Shot2 { get; set; }
    }
}
