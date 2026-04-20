using Cellular.ViewModel;
using SQLite;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Cellular.Data
{
    public class SmartDotDeviceRepository(SQLiteAsyncConnection conn)
    {
        private readonly SQLiteAsyncConnection _conn = conn ?? throw new ArgumentNullException(nameof(conn));

        public Task InitAsync() => _conn.CreateTableAsync<SmartDotDevice>();

        public Task<SmartDotDevice?> GetByMacAsync(string mac) =>
            _conn.Table<SmartDotDevice>().FirstOrDefaultAsync(d =>
                d.MacAddress.ToLower() == mac.ToLower());

        public Task<List<SmartDotDevice>> GetAllAsync() =>
            _conn.Table<SmartDotDevice>().ToListAsync();

        public Task AddAsync(SmartDotDevice device) =>
            _conn.InsertAsync(device);

        public Task UpdateAsync(SmartDotDevice device) =>
            _conn.UpdateAsync(device);

        /// <summary>
        /// Inserts if new, updates if existing (matched by MAC).
        /// Returns the saved record.
        /// </summary>
        public async Task<SmartDotDevice> UpsertAsync(SmartDotDevice device)
        {
            var existing = await GetByMacAsync(device.MacAddress);
            if (existing == null)
            {
                await _conn.InsertAsync(device);
                return device;
            }

            device.Id = existing.Id;
            await _conn.UpdateAsync(device);
            return device;
        }
    }
}
