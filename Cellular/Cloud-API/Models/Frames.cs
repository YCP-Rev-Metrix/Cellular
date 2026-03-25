using System.Text.Json.Serialization;

namespace Cellular.Cloud_API.Models;

/// <summary>POST/GET: send mobileID (our local id); gameId references Game mobileID.</summary>
public class Frames
{
    [JsonPropertyName("id")]
    public int Id { get; set; }
    [JsonPropertyName("mobileID")]
    public int? MobileID { get; set; }
    [JsonPropertyName("gameId")]
    public int GameId { get; set; }
    public int ShotOne { get; set; }
    public int ShotTwo { get; set; }
    public int FrameNumber { get; set; }
    public int Lane { get; set; }
    public int Result { get; set; }
}