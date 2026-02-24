namespace Cellular.Cloud_API.Models;

public class Game
{
    public int Id  { get; set; }
    public String GameNumber { get; set; }
    public String Lanes { get; set; }
    public int Score { get; set; }
    public int Win { get; set; }
    public int StartingLane { get; set; }
    public int SessionId { get; set; }
    public int TeamResult { get; set; }
    public int IndividualResult { get; set; }
}