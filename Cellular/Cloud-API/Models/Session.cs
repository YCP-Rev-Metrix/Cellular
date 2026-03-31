using System.Text.Json.Serialization;

namespace Cellular.Cloud_API.Models;

/// <summary>
/// POST/GET: send mobileID (our local session id). eventId/establishmentId are the cloud server's row ids
/// for the linked event/establishment (see TestServer flow: PostEvent then PostSession with resolved id).
/// GET returns the same shape; the app maps these ids back to local mobile IDs when applying to SQLite.
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