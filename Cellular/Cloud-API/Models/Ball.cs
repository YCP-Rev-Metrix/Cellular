using System.Text.Json.Serialization;

namespace Cellular.Cloud_API.Models;

/// <summary>GET response from server. Server returns weight as int and core (not coreType).</summary>
public class Ball
{
    public int Id { get; set; }
    public int UserId { get; set; }
    [JsonPropertyName("mobileID")]
    public int? MobileID { get; set; }
    public string Name { get; set; }
    public int Weight { get; set; }
    [JsonPropertyName("core")]
    public string Core { get; set; }
    public string BallMFG { get; set; }
    public string BallMFGName { get; set; }
    public string SerialNumber { get; set; }
    public string ColorString { get; set; }
    public string Coverstock { get; set; }
    public string Comment { get; set; }
    public bool Enabled { get; set; }
}

/// <summary>POST body for PostBalls — server accepts weight as int and core.</summary>
public class BallPostRequest
{
    [JsonPropertyName("mobileID")]
    public int MobileID { get; set; }
    public string Name { get; set; }
    public string BallMFG { get; set; }
    public string BallMFGName { get; set; }
    public string SerialNumber { get; set; }
    public int Weight { get; set; }
    [JsonPropertyName("core")]
    public string Core { get; set; }
    public string ColorString { get; set; }
    public string Coverstock { get; set; }
    public string Comment { get; set; }
    public bool Enabled { get; set; }
}
