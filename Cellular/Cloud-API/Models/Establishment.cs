using System.Text.Json.Serialization;

namespace Cellular.Cloud_API.Models;

/// <summary>POST/GET: send mobileID (our local id), retrieve mobileID to keep same id.</summary>
public class Establishment
{
    [JsonPropertyName("id")]
    public int ID { get; set; }
    [JsonPropertyName("mobileID")]
    public int? MobileID { get; set; }
    public string Name { get; set; } 
    public string Lanes { get; set; } 
    public string Type { get; set; } 
    public string Location { get; set; } 
}