﻿using SQLite;
namespace Cellular.ViewModel;


[Table("ball")]
public class Ball
{
    [PrimaryKey, AutoIncrement]
    public int BallId { get; set; }
    public int UserId { get; set; }
    public string Name { get; set; }
    public int SerialNumber { get; set; }
    public int Weight { get;set; }
    public string Core { get; set; }


}
