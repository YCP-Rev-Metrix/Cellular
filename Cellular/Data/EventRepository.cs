using Cellular.ViewModel;
using SQLite;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace Cellular.Data
{
    public class EventRepository
    {
        private readonly SQLiteAsyncConnection _conn;

        public EventRepository(SQLiteAsyncConnection conn)
        {
            _conn = conn ?? throw new ArgumentNullException(nameof(conn));
        }

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

        public async Task<Event?> GetEventByUserIdAndNameAsync(int userID, String name)
        {
            return await _conn.Table<Event>().FirstOrDefaultAsync(u => u.UserId == userID && u.Name == name);
        }
    }
}
