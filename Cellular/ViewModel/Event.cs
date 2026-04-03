using SQLite;
using System.ComponentModel.DataAnnotations;

namespace Cellular.ViewModel
{
    [Table("event")]
    public class Event
    {
        [PrimaryKey, AutoIncrement]
        public int EventId { get; set; }
        [Required]
        public int UserId { get; set; }
        [Required, Unique]
        public string? LongName { get; set; }
        [Required, Unique]
        public string? NickName { get; set; }
        public string? Type { get; set; }
        public string? Location { get; set; }
        public string? StartDate { get; set; }
        public string? EndDate { get; set; }
        public string? WeekDay { get; set; }
        public string? StartTime { get; set; }
        public int NumGamesPerSession { get; set; }
        public int? Average { get; set; }
        public string? Schedule { get; set; }
        public int? Stats { get; set; }
        public string? Standings { get; set; }

        public int? CloudID { get; set; }
        //New
        public bool Enabled { get; set; }
    }
}
