namespace Cellular.Cloud_API;

public class ApiExecutor
{
    public EntityType EntityType { get; set; }
    public OperationType OperationType { get; set; }

    public ApiExecutor(EntityType entityType, OperationType operationType)
    {
        EntityType = entityType;
        OperationType = operationType;
    }

    /// <param name="id">For Frame Get: gameId to append as ?gameId=id</param>
    public string GetUrl(int id = -1)
    {
        string relative = EntityType switch
        {
            EntityType.Ball => OperationType == OperationType.Get ? "GetBallsByUsername" : OperationType == OperationType.Delete ? "DeleteBallsByUsername" : "PostBalls",
            EntityType.Establishment => OperationType == OperationType.Get ? "GetAppEstablishments" : OperationType == OperationType.Delete ? "DeleteAppEstablishments" : "PostEstablishmentApp",
            EntityType.Event => OperationType == OperationType.Get ? "GetEventsByUsername" : OperationType == OperationType.Delete ? "DeleteEventsByUsername" : "PostEvent",
            EntityType.Frame => OperationType == OperationType.Get ? "GetFramesByGameId" : OperationType == OperationType.Delete ? "DeleteAppFrames" : "PostFrames",
            EntityType.Game => OperationType == OperationType.Get ? "GetAppGames" : OperationType == OperationType.Delete ? "DeleteAppGames" : "PostAppGame",
            EntityType.Session => OperationType == OperationType.Get ? "GetAppSessions" : OperationType == OperationType.Delete ? "DeleteAppSessions" : "PostAppSession",
            EntityType.Shot => OperationType == OperationType.Get ? "GetAppShots" : OperationType == OperationType.Delete ? "DeleteAppShots" : "PostAppShot",
            _ => throw new NotImplementedException("This obj type is not implemented yet.")
        };

        string url = OperationType switch
        {
            OperationType.Get => RevMetrixApi.Gets(relative),
            OperationType.Delete => RevMetrixApi.Deletes(relative),
            OperationType.Post => RevMetrixApi.Posts(relative),
            _ => throw new ArgumentOutOfRangeException()
        };

        if (EntityType == EntityType.Frame && OperationType == OperationType.Get && id >= 0)
            url += "?gameId=" + id;

        return url;
    }
}