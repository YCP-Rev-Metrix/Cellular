using Microsoft.Maui.Graphics.Text;
using SQLite;
using System.ComponentModel.DataAnnotations;
namespace Cellular.ViewModel;


[Table("ball")]
public class Ball
{
    [PrimaryKey, AutoIncrement]
    public int BallId { get; set; }
    [Indexed]
    public int UserId { get; set; }
    [Required]
    public string Name { get; set; }
    [Required]
    public string BallMFG { get; set; }
    [Required]
    public string BallMFGName { get; set; }
    public string SerialNumber { get; set; }
    public int Weight { get;set; }
    public string Core { get; set; }
    [Required]
    public string ColorString { get; set; }
    public string Coverstock { get; set; }
    public string Comment { get; set; }
    [Required]
    public bool Enabled { get; set; }

}
