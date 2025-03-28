using SQLite;
namespace Cellular.ViewModel;


[Table("establishment")]
public class Establishment
{
    [PrimaryKey, AutoIncrement]
    public int EstaID { get; set; }
    public int UserId { get; set; }
    public string Name { get; set; }
    public string Lanes { get; set; }
    public string Type { get; set; }
    public string Location { get; set; }


}
