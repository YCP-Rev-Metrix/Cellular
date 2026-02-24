namespace Cellular.Cloud_API.Models;

public class Event
{
    public int Id { get; set; }
    public String Name { get; set; }
    public String Type { get; set; }
    public String Location { get; set; }
    public int Average { get; set; }
    public int Stats { get; set; }
    public String Standings { get; set; }
}