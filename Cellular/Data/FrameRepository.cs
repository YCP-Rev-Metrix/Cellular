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
            Console.WriteLine($"Frame added: Id: {frame.FrameId} Frame Number {frame.FrameNumber}, Shots {frame.Shots}");
            return frame.FrameId;
        }

        // Updates a frame
        public async Task UpdateFrameAsync(BowlingFrame frame)
        {
            await _conn.UpdateAsync(frame);
            Console.WriteLine($"Frame updated: Id: {frame.FrameId} Frame Number {frame.FrameNumber}, Shots {frame.Shots}");
        }

        public async Task<BowlingFrame?> GetFrameById(int frameId)
        {
            return await _conn.Table<BowlingFrame>()
                                  .Where(f => f.FrameId == frameId)
                                  .FirstOrDefaultAsync();
        }

        public async Task<List<int>> GetShotIdsByFrameIdAsync(int frameId)
        {
            // Query the Frame table to get the Shots string by FrameId
            var frame = await _conn.Table<BowlingFrame>().Where(f => f.FrameId == frameId).FirstOrDefaultAsync();

            // If the frame is null or the Shots string is empty, return an empty list
            if (frame == null || string.IsNullOrEmpty(frame.Shots))
            {
                return new List<int>();
            }

            // Split the Shots string by "_" and convert to a list of integers
            return frame.Shots.Split('_')
                              .Where(shot => int.TryParse(shot, out _))  // Ensure valid integers
                              .Select(int.Parse)  // Convert to integers
                              .ToList();
        }
    }
}
