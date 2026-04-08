using System.Text.Json.Serialization;

namespace Cellular.Cloud_API.Models;

/// <summary>
/// POST/GET: send mobileID (our local id); sessionId, ballId, frameId reference other entities' mobileIDs.
/// </summary>
public class Shot
{
    [JsonPropertyName("id")]
    public int ID { get; set; }
    [JsonPropertyName("mobileID")]
    public int? MobileID { get; set; }
    public int Type { get; set; }
    [JsonPropertyName("smartDotId")]
    public int SmartDotID { get; set; }
    [JsonPropertyName("sessionId")]
    public int SessionID { get; set; }
    [JsonPropertyName("ballId")]
    public int BallID { get; set; }
    [JsonPropertyName("frameId")]
    public int FrameID { get; set; }
    public int ShotNumber { get; set; }
    public int LeaveType { get; set; }
    public string Side { get; set; }
    public string Position { get; set; }
    public string Comment { get; set; }
}