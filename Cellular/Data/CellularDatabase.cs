﻿using Cellular.ViewModel;
using SQLite;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Cellular.Data
{
    public class CellularDatabase
    {
        private readonly SQLiteAsyncConnection _database;

        public CellularDatabase()
        {
            var dbPath = Path.Combine(FileSystem.AppDataDirectory, "appdata.db");
            _database = new SQLiteAsyncConnection(dbPath);
        }

        public SQLiteAsyncConnection GetConnection()
        {
            return _database;
        }

        public async Task InitializeAsync()
        {
            await _database.CreateTableAsync<User>();
        }

        public Task<int> SaveBallAsync(Ball ball)
        {
            return _database.InsertAsync(ball);
        }



        public Task<List<Ball>> GetBallsAsync()
        {
            return _database.Table<Ball>().ToListAsync();
        }
    }
}