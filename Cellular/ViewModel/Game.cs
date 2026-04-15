using SQLite;

namespace Cellular.ViewModel
{
    [Table("game")]
    public class Game
    {
        [PrimaryKey, AutoIncrement]
        public int GameId { get; set; }
        public string? Lanes { get; set; }
        public int? GameNumber { get; set; }
        public int? Score { get; set; }
        public bool? Win { get; set; }
        public int? StartingLane { get; set; }
        public int SessionId { get; set; }
        public int? TeamResult { get; set; }
        public int? IndividualResult { get; set; }

        /// <summary>Server-assigned row id from the cloud API. Used to correlate local rows across devices.</summary>
        public int? CloudID { get; set; }

        /// <summary>Display label for pickers — e.g. "UPK - 1". Populated at load time; never persisted.</summary>
        [Ignore]
        public string DisplayLabel { get; set; }
    }
}
