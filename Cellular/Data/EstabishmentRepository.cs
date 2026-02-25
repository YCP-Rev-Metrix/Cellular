using Cellular.ViewModel;
using SQLite;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace Cellular.Data
{
    public class EstablishmentRepository
    {
        private readonly SQLiteAsyncConnection _conn;

        public EstablishmentRepository(SQLiteAsyncConnection conn)
        {
            _conn = conn ?? throw new ArgumentNullException(nameof(conn));
        }

        // Initialize the database and create the table asynchronously
        public async Task InitAsync()
        {
            await _conn.CreateTableAsync<Establishment>();  // Create the table if it doesn't exist
        }

        public async Task AddAsync(Establishment e)
        {
            await _conn.InsertAsync(e);
        }

        public async Task<List<Establishment>> GetEstablishmentsByUserIdAsync(int userID) => await _conn.Table<Establishment>().Where(u => u.UserId == userID).ToListAsync();

        public async Task<Establishment?> GetEstablishmentByNameAsync(String name)
        {
            return await _conn.Table<Establishment>().FirstOrDefaultAsync(u => u.Name == name);
        }
    }
}
