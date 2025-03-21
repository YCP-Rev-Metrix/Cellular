using SQLite;

namespace Cellular.ViewModel
{
    [Table("user")]
    public class User
    {
        [PrimaryKey, AutoIncrement]
        public int UserId { get; set; }

        public string? UserName { get; set; }
        public string? PasswordHash { get; set; }
        public DateTime LastLogin { get; set; }
        public string? BallList { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Email { get; set; }

        public String? PhoneNumber { get; set; }

        public String? Hand { get; set; }
    }
}
