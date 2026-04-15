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
        if (TryGetCiclopesRoute(out var ciclopesRoute))
        {
            return BuildCiclopesUrl(ciclopesRoute);
        }

        var relative = GetRevMetrixAction();
        return OperationType switch
        {
            OperationType.Get => RevMetrixApi.Gets(relative),
            OperationType.Delete => RevMetrixApi.Deletes(relative),
            OperationType.Post => RevMetrixApi.Posts(relative),
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    private bool TryGetCiclopesRoute(out string route)
    {
        route = EntityType switch
        {
            EntityType.CiclopesAggRun => "agg/run",
            EntityType.CiclopesLaneBallsRun => "laneballs/run",
            EntityType.CiclopesFourDBodyRun => "fourdbody/run",
            EntityType.CiclopesLaneBallsQuery => "laneballs/query",
            EntityType.CiclopesFourDBodyQuery => "fourdbody/query",
            _ => string.Empty
        };

        if (string.IsNullOrEmpty(route))
        {
            return false;
        }

        if (OperationType != OperationType.Post)
        {
            throw new NotImplementedException($"{EntityType} only supports POST.");
        }

        return true;
    }

    private static string BuildCiclopesUrl(string route)
    {
        var ciclopesBaseUrl = CiclopesSettings.BaseUrl;
        if (string.IsNullOrWhiteSpace(ciclopesBaseUrl))
        {
            throw new InvalidOperationException(
                "CICLOPES_API_BASE or CICLOPES_IP/CICLOPES_PORT must be configured.");
        }

        return $"{ciclopesBaseUrl}{route}";
    }

    private string GetRevMetrixAction()
    {
        return (EntityType, OperationType) switch
        {
            (EntityType.Ball, OperationType.Get) => "GetBallsByUsername",
            (EntityType.Ball, OperationType.Delete) => "DeleteBallsByUsername",
            (EntityType.Ball, OperationType.Post) => "PostBalls",

            (EntityType.Establishment, OperationType.Get) => "GetAppEstablishments",
            (EntityType.Establishment, OperationType.Delete) => "DeleteAppEstablishments",
            (EntityType.Establishment, OperationType.Post) => "PostEstablishmentApp",

            (EntityType.Event, OperationType.Get) => "GetEventsByUsername",
            (EntityType.Event, OperationType.Delete) => "DeleteEventsByUsername",
            (EntityType.Event, OperationType.Post) => "PostEvents",

            (EntityType.Frame, OperationType.Get) => "GetFramesByGameId",
            (EntityType.Frame, OperationType.Delete) => "DeleteAppFrames",
            (EntityType.Frame, OperationType.Post) => "PostFrames",

            (EntityType.Game, OperationType.Get) => "GetAppGames",
            (EntityType.Game, OperationType.Delete) => "DeleteAppGames",
            (EntityType.Game, OperationType.Post) => "PostAppGame",

            (EntityType.Session, OperationType.Get) => "GetAppSessions",
            (EntityType.Session, OperationType.Delete) => "DeleteAppSessions",
            (EntityType.Session, OperationType.Post) => "PostAppSession",

            (EntityType.Shot, OperationType.Get) => "GetAppShots",
            (EntityType.Shot, OperationType.Delete) => "DeleteAppShots",
            (EntityType.Shot, OperationType.Post) => "PostAppShots",

            _ => throw new NotImplementedException("This obj type is not implemented yet.")
        };
    }
}
