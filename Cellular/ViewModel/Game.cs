using SQLite;

namespace Cellular.ViewModel
{
    [Table("game")]
    public class Game
    {
        [PrimaryKey, AutoIncrement]
        public int GameId { get; set; }
        public int UserId { get; set; }
        public Array? Lanes { get; set; }
        public int? GameNumber { get; set; }
        public int? Score { get; set; }
        public bool? Win { get; set; }
        public int? StartingLane { get; set; }
        public Array? Frames { get; set; }
        public int? TeamResult { get; set; }
        public int? IndividualResult { get; set; }

    }
}
