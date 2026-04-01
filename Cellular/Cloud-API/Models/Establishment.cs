using System.Text.Json.Serialization;

namespace Cellular.Cloud_API.Models;

/// <summary>POST/GET: server uses fullName/nickName fields.</summary>
public class Establishment
{
    [JsonPropertyName("id")]
    public int ID { get; set; }
    [JsonPropertyName("mobileID")]
    public int? MobileID { get; set; }
    public int UserId { get; set; }
    public string FullName { get; set; }
    public string NickName { get; set; }
    public string GPSLocation { get; set; }
    public bool HomeHouse { get; set; }
    public string Reason { get; set; }
    public string Address { get; set; }
    public string PhoneNumber { get; set; }
    public string Lanes { get; set; }
    public string Type { get; set; }
    public string Location { get; set; }
    public bool Enabled { get; set; }
}
