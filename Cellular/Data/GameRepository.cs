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
    public class GameRepository
    {
        private readonly SQLiteAsyncConnection _conn;

        public GameRepository(SQLiteAsyncConnection conn)
        {
            _conn = conn ?? throw new ArgumentNullException(nameof(conn));
        }

        // Initialize the database and create the table asynchronously
        public async Task InitAsync()
        {
            await _conn.CreateTableAsync<Game>();  // Create the table if it doesn't exist
        }
        public async Task AddAsync(Game e)
        {
            await _conn.InsertAsync(e);
            Console.WriteLine($"Game ID: {e.GameId} Game Number: {e.GameNumber}");
        }

        public async Task<List<Game>> GetGamesListBySessionAsync(int sessionId, int userId)
        {
            return await _conn.Table<Game>().Where(g => g.SessionId == sessionId).ToListAsync();
        }

        public async Task<List<Game>> GetGamesByUserIdAsync(int userId)
        {
            var games = await _conn.Table<Game>().ToListAsync();
            var sessions = await _conn.Table<Session>().ToListAsync();

            return (from game in games
                    join session in sessions on game.SessionId equals session.SessionId
                    where session.UserId == userId
                    select game).ToList();
        }

        public async Task UpdateGameAsync(Game game)
        {
            await _conn.UpdateAsync(game);
        }

        public async Task<Game?> GetGame(int sessionId, int gameNumber, int userId)
        {
            return await _conn.Table<Game>()
                                  .Where(g => g.SessionId == sessionId && g.GameNumber == gameNumber)
                                  .FirstOrDefaultAsync();
        }

        public async Task<Game?> GetGameById(int gameId)
        {
            return await _conn.Table<Game>()
                                  .Where(g => g.GameId == gameId)
                                  .FirstOrDefaultAsync();
        }

        public async Task<Game> GetOrCreateGame(int sessionId, int gameNumber)
        {
            var existingGame = await _conn.Table<Game>()
                                          .Where(g => g.SessionId == sessionId && g.GameNumber == gameNumber)
                                          .FirstOrDefaultAsync();

            if (existingGame != null)
            {
                return existingGame;
            }

            // Create new game with defaults
            var newGame = new Game
            {
                SessionId = sessionId,
                GameNumber = gameNumber,
                Score = 0,
                Win = null,
                Lanes = null,
                StartingLane = null,
                TeamResult = null,
                IndividualResult = null,
                CloudID = null
            };

            await _conn.InsertAsync(newGame);
            Debug.WriteLine($"GameRepository: Created new game - GameId {newGame.GameId}, GameNumber {gameNumber}, SessionId {sessionId}");

            return newGame;
        }

        // New: return games in a session that have at least one BowlingFrame with the given frameNumber.
        public async Task<List<Game>> GetGamesBySessionAndFrameNumberAsync(int sessionId, int frameNumber)
        {
            // get frames that match the requested frameNumber
            var frames = await _conn.Table<BowlingFrame>()
                                    .Where(f => f.FrameNumber == frameNumber && f.GameId != null)
                                    .ToListAsync();

            var gameIds = frames.Select(f => f.GameId!.Value).Distinct().ToList();
            if (!gameIds.Any())
                return new List<Game>();

            var games = await _conn.Table<Game>()
                                   .Where(g => g.SessionId == sessionId && gameIds.Contains(g.GameId))
                                   .ToListAsync();

            return games;
        }
    }
}
