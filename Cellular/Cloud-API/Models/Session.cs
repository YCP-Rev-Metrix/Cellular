namespace Cellular.Cloud_API.Models;

public class Session
{
    public int Id { get; set; }
    public int SessionNumber { get; set; }
    public int EstablishmentId { get; set; }
    public int EventId { get; set; }
    public int DateTime { get; set; }
    public String TeamOpponent { get; set; }
    public String IndividualOpponent { get; set; }
    public int Score { get; set; }
    public int Stats { get; set; }
    public int TeamRecord { get; set; }
    public int IndividualRecord { get; set; }
}