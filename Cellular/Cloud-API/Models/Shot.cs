namespace Cellular.Cloud_API.Models;

public class Shot
{
    public int Id { get; set; }
    public int Type { get; set; }
    public int SmartDotId { get; set; }
    public int SessionId { get; set; }
    public int BallId { get; set; }
    public int FrameId { get; set; }
    public int ShotNumber { get; set; }
    public int LeaveType { get; set; }
    public String Side { get; set; }
    public String Position { get; set; }
    public String Comment { get; set; }
}