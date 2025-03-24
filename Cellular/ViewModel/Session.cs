using SQLite;

namespace Cellular.ViewModel
{
    [Table("session")]
    public class Session
    {
        [PrimaryKey, AutoIncrement]
        public int SessionId { get; set; }

        public int? Establishment { get; set; }
        public DateTime DateTime { get; set; }
        public string? TeamOpponent { get; set; }
        public string? IndividualOpponent { get; set; }
        public int? Score { get; set; }
        public int? Stats { get; set; }
        public Array? Games { get; set; }
        public int? TeamRecord { get; set; }
        public int? IndividualRecord { get; set; }

    }
}
