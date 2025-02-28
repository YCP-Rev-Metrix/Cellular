using SQLite;
namespace Cellular.ViewModel;

[Table("user")]
public class User
{
    [PrimaryKey, AutoIncrement] //Column("UserId")
    public int UserId { get; set; }
    public string? UserName { get; set; } // ? means nullable and I did it so it would work with SQLite, We might want to change this
    public  string? Password { get; set; } // ? means nullable and I did it so it would work with SQLite, We might want to change this
    public DateTime LastLogin { get; set; }
    public string? BallList { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
}