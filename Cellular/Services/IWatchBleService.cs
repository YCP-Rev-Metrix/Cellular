using System;
using System.Threading.Tasks;

namespace Cellular.Services
{
    public interface IWatchBleService
    {
        event EventHandler<string> WatchDisconnected;
        event EventHandler<string> WatchJsonReceived;

        bool IsConnected { get; }
        string MacAddress { get; }

        Task<bool> ConnectAsync(object device);
        Task DisconnectAsync();
        Task<bool> SendJsonToWatch(object json);
    }
}