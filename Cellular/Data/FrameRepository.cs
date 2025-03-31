using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SQLite;
using Cellular.ViewModel;

namespace Cellular.Data
{
    public class FrameRepository
    {
        private readonly SQLiteAsyncConnection _conn;

        // Constructor ensures the connection is initialized
        public FrameRepository(SQLiteAsyncConnection conn)
        {
            _conn = conn ?? throw new ArgumentNullException(nameof(conn));
        }

        // Initialize the database table
        public async Task InitAsync()
        {
            await _conn.CreateTableAsync<BowlingFrame>();
        }

        // Adds a frame to the database
        public async Task AddFrame(BowlingFrame frame)
        {
            await _conn.InsertAsync(frame);
        }

        // Updates a frame
        public async Task UpdateFrameAsync(BowlingFrame frame)
        {
            await _conn.UpdateAsync(frame);
        }
    }
}
