using System.Text.Json.Serialization;

namespace Cellular.Cloud_API.Models;

public class CiclopesRunRequest
{
    [JsonPropertyName("video_key")]
    public string VideoKey { get; set; } = string.Empty;

    [JsonPropertyName("sd_key")]
    public string SdKey { get; set; } = string.Empty;
}

public class CiclopesRunResponse
{
    [JsonPropertyName("ball_points")]
    public List<CiclopesBallPoint> BallPoints { get; set; } = [];

    [JsonPropertyName("kinematics_table")]
    public List<CiclopesKinematicsRow> KinematicsTable { get; set; } = [];

    [JsonPropertyName("skeleton_points")]
    public List<List<CiclopesSkeletonPoint>> SkeletonPoints { get; set; } = [];
}

public class LaneBallsRunResponse
{
    [JsonPropertyName("ball_points")]
    public List<CiclopesBallPoint> BallPoints { get; set; } = [];

    [JsonPropertyName("kinematics_table")]
    public List<CiclopesKinematicsRow> KinematicsTable { get; set; } = [];

    [JsonPropertyName("fps")]
    public float Fps { get; set; }
}

public class FourDBodyRunResponse
{
    [JsonPropertyName("skeleton_points")]
    public List<List<CiclopesSkeletonPoint>> SkeletonPoints { get; set; } = [];

    [JsonPropertyName("fps")]
    public float Fps { get; set; }
}

public class CiclopesBallPoint
{
    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }
}

public class CiclopesKinematicsRow
{
    [JsonPropertyName("quarter")]
    public int Quarter { get; set; }

    [JsonPropertyName("start_m")]
    public double StartM { get; set; }

    [JsonPropertyName("end_m")]
    public double EndM { get; set; }

    [JsonPropertyName("mean_speed_mps")]
    public double MeanSpeedMps { get; set; }

    [JsonPropertyName("mean_acceleration_mps2")]
    public double MeanAccelerationMps2 { get; set; }

    [JsonPropertyName("sample_count")]
    public int SampleCount { get; set; }
}

public class CiclopesSkeletonPoint
{
    [JsonPropertyName("joint_id")]
    public int JointId { get; set; }

    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    [JsonPropertyName("z")]
    public double Z { get; set; }
}
