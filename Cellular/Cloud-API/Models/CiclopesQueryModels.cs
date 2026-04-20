using System.Text.Json.Serialization;

namespace Cellular.Cloud_API.Models;

public class CiclopesQueryRequest
{
    [JsonPropertyName("shot_numbers")]
    public List<int> ShotNumbers { get; set; } = [];
}

public class LaneBallsQueryResponse
{
    [JsonPropertyName("shots")]
    public Dictionary<string, LaneBallsShotData> Shots { get; set; } = [];
}

public class LaneBallsShotData
{
    [JsonPropertyName("fps")]
    public float Fps { get; set; }

    [JsonPropertyName("ball_points")]
    public List<CiclopesBallPoint> BallPoints { get; set; } = [];

    [JsonPropertyName("kinematics_table")]
    public List<CiclopesKinematicsRow> KinematicsTable { get; set; } = [];
}

public class FourDBodyQueryResponse
{
    [JsonPropertyName("shots")]
    public Dictionary<string, FourDBodyShotData> Shots { get; set; } = [];
}

public class FourDBodyShotData
{
    [JsonPropertyName("fps")]
    public float Fps { get; set; }

    [JsonPropertyName("skeleton_points")]
    public List<List<CiclopesSkeletonPoint>> SkeletonPoints { get; set; } = [];
}
