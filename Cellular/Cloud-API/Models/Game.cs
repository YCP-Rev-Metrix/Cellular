using System.Text.Json.Serialization;

namespace Cellular.Cloud_API.Models;

/// <summary>
/// POST/GET: send mobileID (our local id); sessionId references Session mobileID.
/// </summary>
public class Game
{
    [JsonPropertyName("id")]
    public int ID { get; set; }
    [JsonPropertyName("mobileID")]
    public int? MobileID { get; set; }
    public string GameNumber { get; set; } = string.Empty; 
    public string Lanes { get; set; } 
    public int Score { get; set; }
    public int Win { get; set; }
    public int StartingLane { get; set; }
    [JsonPropertyName("sessionId")]
    public int SessionID { get; set; }
    public int TeamResult { get; set; }
    public int IndividualResult { get; set; }
}