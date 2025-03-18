using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq; // For LINQ extension methods
using System.Runtime.CompilerServices;

namespace Cellular.ViewModel
{
    internal partial class GameInterfaceViewModel : INotifyPropertyChanged
    {
        private ObservableCollection<object> players;
        private ObservableCollection<object> arsenal;
        private ObservableCollection<object> frames;

        public ObservableCollection<object> Players
        {
            get => players;
            set
            {
                players = value;
                OnPropertyChanged();
            }
        }

        public ObservableCollection<object> Arsenal
        {
            get => arsenal;
            set
            {
                arsenal = value;
                OnPropertyChanged();
            }
        }

        public ObservableCollection<object> Frames
        {
            get => frames;
            set
            {
                frames = value;
                OnPropertyChanged();
            }
        }

        // Property to return only the first 12 frames
        public ObservableCollection<object> First12Frames
        {
            get
            {
                // Returns the first 12 frames, or fewer if there are not enough frames
                return new ObservableCollection<object>(frames.Take(10));
            }
        }

        public GameInterfaceViewModel()
        {
            players = new ObservableCollection<object>
            {
                "Zach",
                "Carson",
                "Thomas",
                "Josh"
            };

            arsenal = new ObservableCollection<object>
            {
                "Ball 1",
                "Ball 2",
                "Ball 3",
                "The Perfect Ball",
                "Ball 5",
                "Ball 6"
            };

            frames = new ObservableCollection<object>
            {
                new Frame(1, 30),
                new Frame(2, 60),
                new Frame(3, 90),
                new Frame(4, 90),
                new Frame(5, 90),
                new Frame(6, 90),
                new Frame(7, 90),
                new Frame(8, 90),
                new Frame(9, 90),
                new Frame(10, 90)

                // Add more frames as needed
            };
        }

        // Notify property changed
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class Frame
    {
        public int FrameNumber { get; set; }
        public int RollingScore { get; set; }

        public Frame(int frameNumber, int rollingScore)
        {
            FrameNumber = frameNumber;
            RollingScore = rollingScore;
        }
    }
}
