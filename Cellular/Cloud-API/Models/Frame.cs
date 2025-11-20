namespace Cellular.Cloud_API.Models;

public class Frame
{
    public int Id { get; set; }
    public int GameId { get; set; }
    public int ShotOne { get; set; }
    public int ShotTwo { get; set; }
    public int FrameNumber { get; set; }
    public int Lane { get; set; }
    public int Result { get; set; }
}