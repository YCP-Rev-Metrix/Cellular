using Microsoft.Maui.Graphics.Text;
using SQLite;
namespace Cellular.ViewModel;


[Table("ball")]
public class Ball
{
    [PrimaryKey, AutoIncrement]
    public int BallId { get; set; }
    public int UserId { get; set; }
    public string Name { get; set; }
    public int Weight { get;set; }
    public string Core { get; set; }
    public string ColorString { get; set; }

    /// <summary>Server-assigned row id from the cloud API (GET/POST). Used to correlate local rows across devices.</summary>
    public int? CloudID { get; set; }
}
