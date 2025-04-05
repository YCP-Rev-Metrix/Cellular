using Cellular.ViewModel;
using Microsoft.Maui.Controls;
using SQLite;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace Cellular.Data
{
    public class SessionRepository(SQLiteAsyncConnection conn)
    {
        private readonly SQLiteAsyncConnection _conn = conn ?? throw new ArgumentNullException(nameof(conn));


        // Initialize the database and create the table asynchronously
        public async Task InitAsync()
        {
            await _conn.CreateTableAsync<Session>();  // Create the table if it doesn't exist
        }
        public async Task AddAsync(Session e)
        {
            await _conn.InsertAsync(e);
            Console.WriteLine($"Session Id: {e.SessionId}, session #: {e.SessionNumber}");
        }
        public async Task<List<Session>> GetSessionsByUserIdAsync(int userID) => await _conn.Table<Session>().Where(u => u.UserId == userID).ToListAsync();
    }
}



