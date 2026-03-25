using System.Text.Json.Serialization;

namespace Cellular.Cloud_API.Models;

/// <summary>
/// POST/GET: send mobileID (our local id); eventId/establishmentId reference other entities' mobileIDs.
/// </summary>
public class Session
{
    [JsonPropertyName("id")]
    public int ID { get; set; }
    [JsonPropertyName("mobileID")]
    public int? MobileID { get; set; }
    public int SessionNumber { get; set; }
    [JsonPropertyName("establishmentId")]
    public int EstablishmentID { get; set; }
    [JsonPropertyName("eventId")]
    public int EventID { get; set; }
    public int DateTime { get; set; }
    public string TeamOpponent { get; set; }
    public string IndividualOpponent { get; set; } 
    public int Score { get; set; }
    public int Stats { get; set; }
    public int TeamRecord { get; set; }
    public int IndividualRecord { get; set; }
}