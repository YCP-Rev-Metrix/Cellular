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

    /// <param name="id">Unused for GET (mobile GETs append <c>?mobileID=</c> via API controller query). Kept for DELETE/POST and API test compatibility.</param>
    public string GetUrl(int id = -1)
    {
        string relative = EntityType switch
        {
            EntityType.Ball => OperationType == OperationType.Get ? "GetBallsByUsername" : OperationType == OperationType.Delete ? "DeleteBallsByUsername" : "PostBalls",
            EntityType.Establishment => OperationType == OperationType.Get ? "GetAllEstablishmentsByUser" : OperationType == OperationType.Delete ? "DeleteAppEstablishments" : "PostEstablishmentApp",
            EntityType.Event => OperationType == OperationType.Get ? "GetEventsByUsername" : OperationType == OperationType.Delete ? "DeleteEventsByUsername" : "PostEvent",
            EntityType.Frame => OperationType == OperationType.Get ? "GetAllFramesByUser" : OperationType == OperationType.Delete ? "DeleteAppFrames" : "PostFrames",
            EntityType.Game => OperationType == OperationType.Get ? "GetAllGamesByUser" : OperationType == OperationType.Delete ? "DeleteAppGames" : "PostAppGame",
            EntityType.Session => OperationType == OperationType.Get ? "GetAllSessionsByUser" : OperationType == OperationType.Delete ? "DeleteAppSessions" : "PostAppSession",
            EntityType.Shot => OperationType == OperationType.Get ? "GetAllShotsByUser" : OperationType == OperationType.Delete ? "DeleteAppShots" : "PostAppShot",
            _ => throw new NotImplementedException("This obj type is not implemented yet.")
        };

        string url = OperationType switch
        {
            OperationType.Get => RevMetrixApi.Gets(relative),
            OperationType.Delete => RevMetrixApi.Deletes(relative),
            OperationType.Post => RevMetrixApi.Posts(relative),
            _ => throw new ArgumentOutOfRangeException()
        };

        return url;
    }
}