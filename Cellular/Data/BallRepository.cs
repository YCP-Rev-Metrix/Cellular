using Cellular.ViewModel;
using SQLite;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace Cellular.Data
{
    public class BallRepository(SQLiteAsyncConnection conn)
    {
        private readonly SQLiteAsyncConnection _conn = conn ?? throw new ArgumentNullException(nameof(conn));


        // Initialize the database and create the table asynchronously
        public async Task InitAsync()
        {
            await _conn.CreateTableAsync<Ball>();  // Create the table if it doesn't exist
        }

        // Add a user to the database asynchronously
        public async Task AddAsync(Ball ball)
        {
            await _conn.InsertAsync(ball);
        }

        // Get all users from the database asynchronously
        public async Task<List<Ball>> GetAllBallsAsync() => await _conn.Table<Ball>().ToListAsync(); 

        // Delete a user from the database by ID asynchronously
        public async Task DeleteAsync(int id)
        {
            var ballToDelete = new Ball { BallId = id };
            await _conn.DeleteAsync(ballToDelete);
        }

        // Update a user's details asynchronously
        public async Task UpdateBallAsync(Ball ball)
        {
            await _conn.UpdateAsync(ball);  // Update the user in the database
        }

        public async Task<Ball?> GetBallByNameAsync(String name)
        {
            return await _conn.Table<Ball>().FirstOrDefaultAsync(u => u.Name == name);
        }

        public async Task<Ball?> GetBallByIdAsync(int ballID)
        {
            return await _conn.Table<Ball>().FirstOrDefaultAsync(u => u.BallId == ballID);
        }

        public async Task<List<Ball>> GetBallsByUserIdAsync(int userID) => await _conn.Table<Ball>().Where(u => u.UserId == userID).ToListAsync();

    }
}
