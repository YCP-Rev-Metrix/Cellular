using Cellular.ViewModel;
using SQLite;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace Cellular.Data
{
    public class EventRepository(SQLiteAsyncConnection conn)
    {
        private readonly SQLiteAsyncConnection _conn = conn ?? throw new ArgumentNullException(nameof(conn));


        // Initialize the database and create the table asynchronously
        public async Task InitAsync()
        {
            await _conn.CreateTableAsync<Event>();  // Create the table if it doesn't exist
        }
        public async Task AddAsync(Event e)
        {
            await _conn.InsertAsync(e);
        }
        public async Task<List<Event>> GetEventsByUserIdAsync(int userID) => await _conn.Table<Event>().Where(u => u.UserId == userID).ToListAsync();

    }
}
