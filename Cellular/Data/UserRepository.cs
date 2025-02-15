using Cellular.ViewModel;
using SQLite;
namespace Cellular.Data;

public class UserRepository(string dbPath)
{
    private readonly string _dbPath = dbPath;
    private SQLiteConnection? conn;

    public void Init()
    {
        conn = new SQLiteConnection(_dbPath);
        conn.CreateTable<User>();
    }
    
    public void Add(User user)
    {
        conn = new SQLiteConnection(_dbPath);
        conn.Insert(user);
    }
    
    public List<User> GetAllUsers()
    {
        Init();
        return [.. conn.Table<User>()];
    }
    public void Delete(int id)
    {
        conn = new SQLiteConnection(_dbPath);
        conn.Delete(new User {UserId=id});
        
    }

    public void EditBallList(User user, string ballList)
    {   
        user.BallList = ballList;
        conn = new SQLiteConnection(_dbPath);
        conn.Update(user);
    }
}