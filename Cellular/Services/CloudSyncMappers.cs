using System;
using System.Collections.Generic;
using System.Linq;
using Cellular.Cloud_API.Models;
using CloudBall = Cellular.Cloud_API.Models.Ball;
using CloudBallPost = Cellular.Cloud_API.Models.BallPostRequest;
using CloudEstablishment = Cellular.Cloud_API.Models.Establishment;
using CloudEvent = Cellular.Cloud_API.Models.Event;
using CloudSession = Cellular.Cloud_API.Models.Session;
using CloudGame = Cellular.Cloud_API.Models.Game;
using CloudFrames = Cellular.Cloud_API.Models.Frames;
using CloudShot = Cellular.Cloud_API.Models.Shot;

namespace Cellular.Services;

/// <summary>
/// Maps between Cloud-API models and local ViewModel/entity types for sync.
/// </summary>
public static class CloudSyncMappers
{
    // --- Ball (send/retrieve mobileID = our local BallId) ---
    public static CloudBall ToCloud(this Cellular.ViewModel.Ball local, int userId) => new CloudBall
    {
        MobileID = local.BallId,
        UserId = userId,
        Name = local.Name ?? "",
        Weight = local.Weight,
        Core = local.Core ?? "",
        BallMFG = local.BallMFG ?? "",
        BallMFGName = local.BallMFGName ?? "",
        SerialNumber = local.SerialNumber ?? "",
        ColorString = local.ColorString ?? "",
        Coverstock = local.Coverstock ?? "",
        Comment = local.Comment ?? "",
        Enabled = local.Enabled
    };

    /// <summary>POST body for PostBalls — server accepts weight as int and core.</summary>
    public static CloudBallPost ToCloudPost(this Cellular.ViewModel.Ball local) => new CloudBallPost
    {
        MobileID = local.BallId,
        Name = local.Name ?? "",
        BallMFG = local.BallMFG ?? "",
        BallMFGName = local.BallMFGName ?? "",
        SerialNumber = local.SerialNumber ?? "",
        Weight = local.Weight,
        Core = local.Core ?? "",
        ColorString = local.ColorString ?? "",
        Coverstock = local.Coverstock ?? "",
        Comment = local.Comment ?? "",
        Enabled = local.Enabled
    };

    public static Cellular.ViewModel.Ball ToLocal(this CloudBall cloud, int userId) => new Cellular.ViewModel.Ball
    {
        BallId = cloud.MobileID ?? cloud.Id,
        UserId = userId,
        Name = cloud.Name ?? "",
        Weight = cloud.Weight,
        Core = cloud.Core ?? "",
        BallMFG = cloud.BallMFG ?? "",
        BallMFGName = cloud.BallMFGName ?? "",
        SerialNumber = cloud.SerialNumber ?? "",
        ColorString = cloud.ColorString ?? "",
        Coverstock = cloud.Coverstock ?? "",
        Comment = cloud.Comment ?? "",
        Enabled = cloud.Enabled,
        CloudID = cloud.Id > 0 ? cloud.Id : null
    };

    // --- Establishment (send/retrieve mobileID = our local EstaID) ---
    public static CloudEstablishment ToCloud(this Cellular.ViewModel.Establishment local, int userId) => new CloudEstablishment
    {
        MobileID = local.EstaID,
        UserId = userId,
        FullName = local.FullName ?? "",
        NickName = local.NickName ?? "",
        GPSLocation = local.GPSLocation ?? "",
        HomeHouse = local.HomeHouse,
        Reason = local.Reason ?? "",
        Address = local.Address ?? "",
        PhoneNumber = local.PhoneNumber ?? "",
        Lanes = local.Lanes ?? "",
        Type = local.Type ?? "",
        Location = local.Location ?? "",
        Enabled = local.Enabled
    };

    public static Cellular.ViewModel.Establishment ToLocal(this CloudEstablishment cloud, int userId) => new Cellular.ViewModel.Establishment
    {
        EstaID = cloud.MobileID ?? cloud.ID,
        UserId = userId,
        FullName = cloud.FullName ?? "",
        NickName = cloud.NickName ?? "",
        GPSLocation = cloud.GPSLocation ?? "",
        HomeHouse = cloud.HomeHouse,
        Reason = cloud.Reason ?? "",
        Address = cloud.Address ?? "",
        PhoneNumber = cloud.PhoneNumber ?? "",
        Lanes = cloud.Lanes ?? "",
        Type = cloud.Type ?? "",
        Location = cloud.Location ?? "",
        Enabled = cloud.Enabled,
        CloudID = cloud.ID > 0 ? cloud.ID : null
    };

    // --- Event (send/retrieve mobileID = our local EventId; sessions reference eventId) ---
    public static CloudEvent ToCloud(this Cellular.ViewModel.Event local, int userId) => new CloudEvent
    {
        MobileID = local.EventId,
        UserId = userId,
        LongName = local.LongName ?? "",
        NickName = local.NickName ?? "",
        Type = local.Type ?? "",
        Location = local.Location ?? "",
        StartDate = local.StartDate ?? "",
        EndDate = local.EndDate ?? "",
        WeekDay = local.WeekDay ?? "",
        StartTime = local.StartTime ?? "",
        NumGamesPerSession = local.NumGamesPerSession,
        Average = local.Average ?? 0,
        Schedule = local.Schedule ?? "",
        Stats = local.Stats ?? 0,
        Standings = local.Standings ?? "",
        Enabled = local.Enabled
    };

    public static Cellular.ViewModel.Event ToLocal(this CloudEvent cloud, int userId) => new Cellular.ViewModel.Event
    {
        EventId = cloud.MobileID ?? cloud.Id,
        UserId = userId,
        LongName = cloud.LongName ?? "",
        NickName = cloud.NickName ?? "",
        Type = cloud.Type ?? "",
        Location = cloud.Location ?? "",
        StartDate = cloud.StartDate ?? "",
        EndDate = cloud.EndDate ?? "",
        WeekDay = cloud.WeekDay ?? "",
        StartTime = cloud.StartTime ?? "",
        NumGamesPerSession = cloud.NumGamesPerSession,
        Average = cloud.Average,
        Schedule = cloud.Schedule ?? "",
        Stats = cloud.Stats,
        Standings = cloud.Standings ?? "",
        Enabled = cloud.Enabled,
        CloudID = cloud.Id > 0 ? cloud.Id : null
    };

    // --- Session (mobileID = local SessionId; establishmentId/eventId = cloud server ids for FK) ---
    public static CloudSession ToCloud(this Cellular.ViewModel.Session local, int establishmentServerId, int eventServerId) => new CloudSession
    {
        MobileID = local.SessionId,
        SessionNumber = local.SessionNumber,
        EstablishmentID = establishmentServerId,
        EventID = eventServerId,
        DateTime = local.DateTime.HasValue ? (int)local.DateTime.Value.Subtract(DateTime.UnixEpoch).TotalSeconds : 0,
        TeamOpponent = local.TeamOpponent ?? "",
        IndividualOpponent = local.IndividualOpponent ?? "",
        Score = local.Score ?? 0,
        Stats = local.Stats ?? 0,
        TeamRecord = local.TeamRecord ?? 0,
        IndividualRecord = local.IndividualRecord ?? 0
    };

    /// <param name="eventServerIdToLocalMobileId">Cloud event PK → local event mobileID (EventId).</param>
    /// <param name="establishmentServerIdToLocalMobileId">Cloud establishment PK → local EstaID.</param>
    public static Cellular.ViewModel.Session ToLocal(
        this CloudSession cloud,
        int userId,
        IReadOnlyDictionary<int, int>? eventServerIdToLocalMobileId = null,
        IReadOnlyDictionary<int, int>? establishmentServerIdToLocalMobileId = null)
    {
        var localEventId = cloud.EventID;
        if (eventServerIdToLocalMobileId != null && eventServerIdToLocalMobileId.TryGetValue(cloud.EventID, out var le))
            localEventId = le;

        int? localEst = cloud.EstablishmentID > 0 ? cloud.EstablishmentID : null;
        if (localEst is > 0 && establishmentServerIdToLocalMobileId != null &&
            establishmentServerIdToLocalMobileId.TryGetValue(cloud.EstablishmentID, out var lest))
            localEst = lest;

        return new Cellular.ViewModel.Session
        {
        SessionId = cloud.MobileID ?? cloud.ID,
        UserId = userId,
        SessionNumber = cloud.SessionNumber,
        EventId = localEventId,
        Establishment = localEst > 0 ? localEst : null,
        DateTime = DateTime.UnixEpoch.AddSeconds(cloud.DateTime),
        TeamOpponent = cloud.TeamOpponent ?? "",
        IndividualOpponent = cloud.IndividualOpponent ?? "",
        Score = cloud.Score,
        Stats = cloud.Stats,
        TeamRecord = cloud.TeamRecord,
        IndividualRecord = cloud.IndividualRecord,
        CloudID = cloud.ID > 0 ? cloud.ID : null
        };
    }

    // --- Game (send/retrieve mobileID = our local GameId; sessionId = Session mobileID) ---
    public static CloudGame ToCloud(this Cellular.ViewModel.Game local, int _) => new CloudGame
    {
        MobileID = local.GameId,
        GameNumber = (local.GameNumber ?? 0).ToString(),
        Lanes = local.Lanes ?? "",
        Score = local.Score ?? 0,
        Win = (local.Win ?? false) ? 1 : 0,
        StartingLane = local.StartingLane ?? 0,
        SessionID = local.SessionId,
        TeamResult = local.TeamResult ?? 0,
        IndividualResult = local.IndividualResult ?? 0
    };

    public static Cellular.ViewModel.Game ToLocal(this CloudGame cloud, int _) => new Cellular.ViewModel.Game
    {
        GameId = cloud.MobileID ?? cloud.ID,
        GameNumber = int.TryParse(cloud.GameNumber, out var gn) ? gn : (int?)null,
        Lanes = cloud.Lanes ?? "",
        Score = cloud.Score,
        Win = cloud.Win != 0,
        StartingLane = cloud.StartingLane,
        SessionId = cloud.SessionID,
        TeamResult = cloud.TeamResult,
        IndividualResult = cloud.IndividualResult,
        CloudID = cloud.ID > 0 ? cloud.ID : null
    };

    // --- Frame (send/retrieve mobileID = our local FrameId; gameId = Game mobileID) ---
    public static CloudFrames ToCloud(this Cellular.ViewModel.BowlingFrame local, int _) => new CloudFrames
    {
        MobileID = local.FrameId,
        GameId = local.GameId ?? 0,
        ShotOne = local.Shot1 ?? 0,
        ShotTwo = local.Shot2 ?? 0,
        FrameNumber = local.FrameNumber ?? 0,
        Lane = local.Lane ?? 0,
        Result = int.TryParse(local.Result, out var r) ? r : 0
    };

    public static Cellular.ViewModel.BowlingFrame ToLocal(this CloudFrames cloud, int _) => new Cellular.ViewModel.BowlingFrame
    {
        FrameId = cloud.MobileID ?? cloud.Id,
        GameId = cloud.GameId,
        Shot1 = cloud.ShotOne,
        Shot2 = cloud.ShotTwo,
        FrameNumber = cloud.FrameNumber,
        Lane = cloud.Lane,
        Result = cloud.Result.ToString(),
        CloudID = cloud.Id > 0 ? cloud.Id : null
    };

    // --- Shot (send/retrieve mobileID = our local ShotId; sessionId/ballId/frameId = other entities' mobileIDs) ---
    public static CloudShot ToCloud(this Cellular.ViewModel.Shot local, int sessionId) => new CloudShot
    {
        MobileID = local.ShotId,
        Type = 0,
        SmartDotID = 0,
        SessionID = sessionId,
        BallID = local.Ball ?? 0,
        FrameID = local.Frame ?? 0,
        ShotNumber = local.ShotNumber ?? 0,
        LeaveType = local.LeaveType ?? 0,
        Side = local.Side ?? "",
        Position = local.Position ?? "",
        Comment = local.Comment ?? ""
    };

    public static Cellular.ViewModel.Shot ToLocal(this CloudShot cloud, int _) => new Cellular.ViewModel.Shot
    {
        ShotId = cloud.MobileID ?? cloud.ID,
        Ball = cloud.BallID,
        Frame = cloud.FrameID,
        ShotNumber = cloud.ShotNumber,
        LeaveType = (short)cloud.LeaveType,
        Side = cloud.Side ?? "",
        Position = cloud.Position ?? "",
        Comment = cloud.Comment ?? "",
        CloudID = cloud.ID > 0 ? cloud.ID : null
    };
}
