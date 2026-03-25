using System.Text.Json.Serialization;

namespace Cellular.Cloud_API.Models;

/// <summary>POST/GET: send mobileID (our local id), retrieve mobileID so EventID on sessions matches.</summary>
public class Event
{
    [JsonPropertyName("id")]
    public int Id { get; set; }
    [JsonPropertyName("mobileID")]
    public int? MobileID { get; set; }
    public int UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public int Average { get; set; }
    public int Stats { get; set; }
    public string Standings { get; set; } = string.Empty;
}