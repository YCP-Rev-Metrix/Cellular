using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SQLite;
using Cellular.ViewModel;

namespace Cellular.Data
{
    public class FrameRepository(SQLiteAsyncConnection conn)
    {
        private readonly SQLiteAsyncConnection _conn = conn ?? throw new ArgumentNullException(nameof(conn));

        // Initialize the database table
        public async Task InitAsync()
        {
            await _conn.CreateTableAsync<BowlingFrame>();
        }

        // Adds a frame to the database
        public async Task<int> AddFrame(BowlingFrame frame)
        {
            await _conn.InsertAsync(frame);
            return frame.FrameId;
        }

        // Updates a frame
        public async Task UpdateFrameAsync(BowlingFrame frame)
        {
            await _conn.UpdateAsync(frame);
        }

        public async Task<BowlingFrame?> GetFrameById(int frameId)
        {
            return await _conn.Table<BowlingFrame>()
                                  .Where(f => f.FrameId == frameId)
                                  .FirstOrDefaultAsync();
        }


    }
}
