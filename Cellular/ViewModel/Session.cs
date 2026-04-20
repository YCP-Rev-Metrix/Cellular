using SQLite;

namespace Cellular.ViewModel
{
    [Table("session")]
    public class Session
    {
        [PrimaryKey, AutoIncrement]
        public int SessionId { get; set; }
        public int SessionNumber { get; set; }
        public int UserId { get; set; }
        public int EventId { get; set; }
        public int? Establishment { get; set; }
        public DateTime? DateTime { get; set; }
        public string? TeamOpponent { get; set; }
        public string? IndividualOpponent { get; set; }
        public int? Score { get; set; }
        public int? Stats { get; set; }
        public int? TeamRecord { get; set; }
        public int? IndividualRecord { get; set; }

        /// <summary>Server-assigned row id from the cloud API. Used to correlate local rows across devices.</summary>
        public int? CloudID { get; set; }

        /// <summary>
        /// Non-persisted display label built after loading:
        /// "{EventNickName}, {date}, Week {SessionNumber}"
        /// </summary>
        [Ignore]
        public string DisplayName { get; set; }

        /// <summary>Builds and assigns DisplayName from the supplied event nick-name.</summary>
        public void BuildDisplayName(string eventNickName)
        {
            var nick = string.IsNullOrWhiteSpace(eventNickName) ? "?" : eventNickName;
            var date = DateTime.HasValue ? DateTime.Value.ToString("MM/dd/yy") : "–";
            DisplayName = $"{nick}, {date}, Week {SessionNumber}";
        }
    }
}
