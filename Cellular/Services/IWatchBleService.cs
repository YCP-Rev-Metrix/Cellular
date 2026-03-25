using System;
using System.Threading.Tasks;
using Cellular.Data;
using Cellular.ViewModel;

namespace Cellular.Services
{
    public interface IWatchBleService
    {
        event EventHandler<string> WatchDisconnected;
        event EventHandler<string> WatchJsonReceived;
        event EventHandler? WatchStartRecordingRequested;
        event EventHandler? WatchStopRecordingRequested;

        bool IsConnected { get; }
        string MacAddress { get; }

        Task<bool> ConnectAsync(object device);
        Task DisconnectAsync();
        Task<bool> SendJsonToWatch(int userId, SessionRepository? sessionRepo, BallRepository? ballRepo, EventRepository? eventRepo, GameRepository? gameRepo, User? user, FrameRepository? frameRepo, ShotRepository? shotRepo);
    }
}