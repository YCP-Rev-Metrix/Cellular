using Cellular.ViewModel;
using SQLite;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace Cellular.Data
{
    public class UserRepository(SQLiteAsyncConnection conn)
    {
        private readonly SQLiteAsyncConnection _conn = conn ?? throw new ArgumentNullException(nameof(conn));


        // Initialize the database and create the table asynchronously
        public async Task InitAsync()
        {
            await _conn.CreateTableAsync<User>();  // Create the table if it doesn't exist
        }

        // Add a user to the database asynchronously
        public async Task AddAsync(User user)
        {
            await _conn.InsertAsync(user);
        }

        // Get all users from the database asynchronously
        public async Task<List<User>> GetAllUsersAsync() => await _conn.Table<User>().ToListAsync();  // Retrieve all users from the table

        // Delete a user from the database by ID asynchronously
        public async Task DeleteAsync(int id)
        {
            var userToDelete = new User { UserId = id };
            await _conn.DeleteAsync(userToDelete);
        }

        // Update a user's details asynchronously
        public async Task UpdateUserAsync(User user)
        {
            await _conn.UpdateAsync(user);  // Update the user in the database
        }

        // Get a user by credentials asynchronously
        public async Task<User?> GetUserByCredentialsAsync(string username, string password)
        {
            return await _conn.Table<User>().FirstOrDefaultAsync(u => u.UserName == username && u.Password == password);
        }

        // Get a user by username asynchronously
        public async Task<User?> GetUserByUsernameAsync(string username)
        {

            return await _conn.Table<User>().FirstOrDefaultAsync(u => u.UserName == username);
        }


        // Get a user by email asynchronously
        public async Task<User?> GetUserByEmailAsync(string email)
        {
            return await _conn.Table<User>().FirstOrDefaultAsync(u => u.Email == email);
        }
    }
}
