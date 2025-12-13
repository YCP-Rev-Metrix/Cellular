using System.Runtime.InteropServices.JavaScript;

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
    
    public String GetUrl()
    {
        string url = "https://api.revmetrix.io/api/";
        
        // Use constructor args to execute endpoints
        switch (EntityType)
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
            default:
                throw new NotImplementedException("This obj type is not implemented yet.");
        }

        return url;
    }
}