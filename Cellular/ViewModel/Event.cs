using SQLite;
namespace Cellular.ViewModel;


[Table("event")]
public class Event
{
    [PrimaryKey]
    public int EventId { get; set; }
    public int UserId { get; set; } 
    public string Name { get; set; }
    public string Type { get; set; }
    public string Establishment { get; set; }
    public string Sessions { get; set; }
    public string Standing { get; set; }


}

