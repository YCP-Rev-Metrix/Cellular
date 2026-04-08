using SQLite;
using System.ComponentModel.DataAnnotations;
namespace Cellular.ViewModel;


[Table("establishment")]
public class Establishment
{
    [PrimaryKey, AutoIncrement]
    public int EstaID { get; set; }
    [Required]
    public int UserId { get; set; }
    [Required]
    public string FullName { get; set; }
    [Required, Unique]
    public string NickName { get; set; }
    public string GPSLocation { get; set; }
    public bool HomeHouse { get; set; }
    [Required]
    public string Reason { get; set; }
    public string Address { get; set; }
    public string PhoneNumber { get; set; }
    public string Lanes { get; set; }
    public string Type { get; set; }
    public string Location { get; set; }

    public int? CloudID { get; set; }
    //New
    public bool Enabled { get; set; }
}
