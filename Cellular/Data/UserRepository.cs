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
            return await _conn.Table<User>().FirstOrDefaultAsync(u => u.UserName == username && u.PasswordHash == password);
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

        public async Task<User?> GetUserByIdAsync(int userId)
        {
            return await _conn.Table<User>().FirstOrDefaultAsync(u => u.UserId == userId);
        }

        /// <summary>
        /// Updates the SmartDotMac address for a user
        /// </summary>
        public async Task UpdateSmartDotMacAsync(int userId, string? macAddress)
        {
            var user = await GetUserByIdAsync(userId);
            if (user != null)
            {
                user.SmartDotMac = macAddress;
                await UpdateUserAsync(user);
            }
        }

        /// <summary>
        /// Gets the SmartDotMac address for a user
        /// </summary>
        public async Task<string?> GetSmartDotMacAsync(int userId)
        {
            var user = await GetUserByIdAsync(userId);
            return user?.SmartDotMac;
        }

        /// <summary>
        /// Updates the IsConnected status for a user
        /// </summary>
        public async Task UpdateIsConnectedAsync(int userId, bool isConnected)
        {
            var user = await GetUserByIdAsync(userId);
            if (user != null)
            {
                user.IsConnected = isConnected;
                await UpdateUserAsync(user);
            }
        }

        /// <summary>
        /// Gets the IsConnected status for a user
        /// </summary>
        public async Task<bool> GetIsConnectedAsync(int userId)
        {
            var user = await GetUserByIdAsync(userId);
            return user?.IsConnected ?? false;
        }

    }
}
