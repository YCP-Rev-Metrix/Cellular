using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SQLite;
using Cellular.ViewModel;
using System.Diagnostics;

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
            Console.WriteLine($"Frame added: Id: {frame.FrameId} Frame Number {frame.FrameNumber}, Shot1 {frame.Shot1}, Shot2 {frame.Shot2}");
            return frame.FrameId;
        }

        // Updates a frame
        public async Task UpdateFrameAsync(BowlingFrame frame)
        {
            await _conn.UpdateAsync(frame);
            Console.WriteLine($"Frame updated: Id: {frame.FrameId} Frame Number {frame.FrameNumber}, Shots {frame.Shot1}");
        }

        public async Task<BowlingFrame?> GetFrameById(int frameId)
        {
            return await _conn.Table<BowlingFrame>()
                                  .Where(f => f.FrameId == frameId)
                                  .FirstOrDefaultAsync();
        }

        public async Task<List<int>> GetShotIdsByFrameIdAsync(int frameId)
        {
            var frame = await _conn.Table<BowlingFrame>()
                                   .Where(f => f.FrameId == frameId)
                                   .FirstOrDefaultAsync();

            if (frame == null)
            {
                return new List<int>();
            }

            var shots = new List<int>();

            if (frame.Shot1.HasValue)
                shots.Add(frame.Shot1.Value);

            if (frame.Shot2.HasValue)
                shots.Add(frame.Shot2.Value);

            return shots;
        }


        public async Task<List<int>> GetFrameIdsByGameIdAsync(int gameId)
        {
            var frameIds = await _conn.Table<BowlingFrame>()
                                       .Where(f => f.GameId == gameId)
                                       .ToListAsync();

            Debug.WriteLine($"Frame ids: {frameIds.Select(f => f.FrameId)}");

            return frameIds.Select(f => f.FrameId).ToList();
        }
    }
}
