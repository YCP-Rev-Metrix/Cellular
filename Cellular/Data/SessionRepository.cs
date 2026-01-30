using Cellular.ViewModel;
using Microsoft.Maui.Controls;
using SQLite;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Globalization;

namespace Cellular.Data
{
    public class SessionRepository
    {
        private readonly SQLiteAsyncConnection _conn;

        public SessionRepository(SQLiteAsyncConnection conn)
        {
            _conn = conn ?? throw new ArgumentNullException(nameof(conn));
        }

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

        public async Task<List<Session>> GetSessionsByUserIdAsync(int userID) 
            => await _conn.Table<Session>().Where(u => u.UserId == userID).ToListAsync();

        // New: get sessions for user constrained by optional start/end dates.
        // Session.DateTime is stored as string (e.g. "MM/dd/yyyy") — try to parse, ignore unparsable entries.
        public async Task<List<Session>> GetSessionsByUserIdAndDateRangeAsync(int userId, DateTime? start, DateTime? end)
        {
            var all = await GetSessionsByUserIdAsync(userId);
            if (start == null && end == null) return all;

            var filtered = new List<Session>();
            foreach (var s in all)
            {
                if (string.IsNullOrWhiteSpace(s.DateTime))
                    continue;

                // attempt to parse; accept common formats
                if (!DateTime.TryParseExact(s.DateTime, new[] { "MM/dd/yyyy", "M/d/yyyy", "yyyy-MM-dd" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                {
                    // fallback to general parse
                    if (!DateTime.TryParse(s.DateTime, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
                        continue;
                }

                if (start.HasValue && parsed < start.Value.Date) continue;
                if (end.HasValue && parsed > end.Value.Date) continue;

                filtered.Add(s);
            }

            return filtered;
        }
    }
}



