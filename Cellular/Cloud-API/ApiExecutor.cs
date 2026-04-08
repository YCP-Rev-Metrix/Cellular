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
            case EntityType.Ball:
                if (OperationType == OperationType.Get)
                {
                    url += "gets/GetBallsByUsername";
                }
                else if (OperationType == OperationType.Post)
                {
                    url += "posts/PostBalls";
                }
                break;
            case EntityType.Establishment:
                if (OperationType == OperationType.Get)
                {
                    url += "gets/GetAppEstablishments";
                }
                else if (OperationType == OperationType.Post)
                {
                    url += "posts/PostEstablishmentApp";
                }
                break;
            case EntityType.Event:
                if (OperationType == OperationType.Get)
                {
                    url += "gets/GetEventsByUsername";
                }
                else if (OperationType == OperationType.Post)
                {
                    url += "posts/PostEvents";
                }
                break;
            case EntityType.Frame:
                if (OperationType == OperationType.Get)
                {
                    url += "gets/GetFramesByGameId";
                }
                else if (OperationType == OperationType.Post)
                {
                    url += "posts/PostFrames";
                }
                break;
            case EntityType.Game:
                if (OperationType == OperationType.Get)
                {
                    url += "gets/GetAppGames";
                }
                else if (OperationType == OperationType.Post)
                {
                    url += "posts/PostAppGame";
                }
                break;
            case EntityType.Session:
                if (OperationType == OperationType.Get)
                {
                    url += "gets/GetAppSessions";
                }
                else if (OperationType == OperationType.Post)
                {
                    url += "posts/PostAppSession";
                }
                break;
            case EntityType.Shot:
                if (OperationType == OperationType.Get)
                {
                    url += "gets/GetAppShots";
                }
                else if (OperationType == OperationType.Post)
                {
                    url += "posts/PostAppShots";
                }
                break;
            case EntityType.CiclopesAggRun:
                if (OperationType == OperationType.Post)
                {
                    string? ciclopesBaseUrl = CiclopesSettings.BaseUrl;
                    if (string.IsNullOrWhiteSpace(ciclopesBaseUrl))
                    {
                        throw new InvalidOperationException("CICLOPES_IP and CICLOPES_PORT must be configured.");
                    }

                    url = ciclopesBaseUrl + "agg/run";
                }
                else
                {
                    throw new NotImplementedException("CiclopesAggRun only supports POST.");
                }
                break;
            case EntityType.CiclopesLaneBallsRun:
                if (OperationType == OperationType.Post)
                {
                    string? laneBallsBaseUrl = CiclopesSettings.BaseUrl;
                    if (string.IsNullOrWhiteSpace(laneBallsBaseUrl))
                    {
                        throw new InvalidOperationException("CICLOPES_IP and CICLOPES_PORT must be configured.");
                    }

                    url = laneBallsBaseUrl + "laneballs/run";
                }
                else
                {
                    throw new NotImplementedException("CiclopesLaneBallsRun only supports POST.");
                }
                break;
            case EntityType.CiclopesFourDBodyRun:
                if (OperationType == OperationType.Post)
                {
                    string? fourDBodyBaseUrl = CiclopesSettings.BaseUrl;
                    if (string.IsNullOrWhiteSpace(fourDBodyBaseUrl))
                    {
                        throw new InvalidOperationException("CICLOPES_IP and CICLOPES_PORT must be configured.");
                    }

                    url = fourDBodyBaseUrl + "fourdbody/run";
                }
                else
                {
                    throw new NotImplementedException("CiclopesFourDBodyRun only supports POST.");
                }
                break;
            default:
                throw new NotImplementedException("This obj type is not implemented yet.");
        }
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


