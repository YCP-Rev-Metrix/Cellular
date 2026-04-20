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
        event EventHandler<Shot>? WatchShotReceived;

        bool IsConnected { get; }
        string MacAddress { get; }

        Task<bool> ConnectAsync(object device);
        Task DisconnectAsync();
        Task<bool> SendJsonToWatch(int userId, SessionRepository? sessionRepo, BallRepository? ballRepo, EventRepository? eventRepo, GameRepository? gameRepo, User? user, FrameRepository? frameRepo, ShotRepository? shotRepo, EstablishmentRepository? establishmentRepo = null, int? specificSessionId = null);
        void SetRepositories(GameRepository gameRepo, FrameRepository frameRepo, ShotRepository shotRepo, 
            SessionRepository? sessionRepo = null, BallRepository? ballRepo = null, EventRepository? eventRepo = null,
            User? user = null, int userId = 0, EstablishmentRepository? establishmentRepo = null);
    }
}