using System.Text.Json.Serialization;

namespace Cellular.Cloud_API.Models;

/// <summary>GET response; use mobileID ?? id as local id. POST can include mobileID.</summary>
public class Ball
{
    public int Id { get; set; }
    public int UserId { get; set; }
    [JsonPropertyName("mobileID")]
    public int? MobileID { get; set; }
    public string Name { get; set; } 
    public string Weight { get; set; } 
    public string CoreType { get; set; } 
}

/// <summary>POST body for PostBalls (TestServer.py: mobileID, name, weight, coreType).</summary>
public class BallPostRequest
{
    [JsonPropertyName("mobileID")]
    public int MobileID { get; set; }
    public string Name { get; set; } 
    public string Weight { get; set; } 
    public string CoreType { get; set; } 
}