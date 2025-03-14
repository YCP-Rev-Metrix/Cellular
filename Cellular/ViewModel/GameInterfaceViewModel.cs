using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Maui.Layouts;
using Microsoft.Maui.Storage;

namespace Cellular.ViewModel
{
    internal partial class GameInterfaceViewModel : INotifyPropertyChanged
    {
        private ObservableCollection<object> players;
        private ObservableCollection<object> arsenal;
        private ObservableCollection<object> frames;

        public ObservableCollection<object> Players
        {
            get
            {
                return players;
            }
            set
            {
                players = value;
            }
        }

        public ObservableCollection<object> Arsenal
        {
            get
            {
                return arsenal;
            }
            set
            {
                arsenal = value;
            }
        }

        public ObservableCollection<object> Frames
        {
            get
            {
                return frames;
            }
            set
            {
                frames = value;
            }
        }

        public GameInterfaceViewModel()
        {
            players = new ObservableCollection<object>();
            players.Add("Zac");
            players.Add("Carson");
            players.Add("Thomas");
            players.Add("Josh");


            arsenal = new ObservableCollection<object>();
            arsenal.Add("Ball 1");
            arsenal.Add("Ball 2");
            arsenal.Add("Ball 3");
            arsenal.Add("The Perfect Ball");
            arsenal.Add("Ball 5");
            arsenal.Add("Ball 6");

            frames = new ObservableCollection<object>();
            frames.Add(new Frame(1, 30));
            frames.Add(new Frame(2, 60));
            frames.Add(new Frame(3, 90));
            frames.Add(new Frame(4, 30));
            frames.Add(new Frame(5, 60));
            frames.Add(new Frame(6, 90));
            frames.Add(new Frame(7, 30));
            frames.Add(new Frame(8, 60));
            frames.Add(new Frame(9, 90));
            frames.Add(new Frame(10, 30));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
    public class Frame
    {
        public int frameNumber { get; set; }
        public int rollingScore { get; set; }

        public Frame(int frameNumber, int rollingScore)
        {
            this.frameNumber = frameNumber;
            this.rollingScore = rollingScore;
        }
    }
}
