using SQLite;

namespace Cellular.ViewModel
{
    [Table("event")]
    public class Event
    {
        [PrimaryKey, AutoIncrement]
        public int EventId { get; set; }
        public int UserId { get; set; }
        public string? Name { get; set; }
        public string? Type { get; set; }
        public string? Location { get; set; }
        public String? Sessions { get; set; }
        public int? Average { get; set; }
        public int? Stats { get; set; }
        public string? Standings { get; set; }
    }
}
