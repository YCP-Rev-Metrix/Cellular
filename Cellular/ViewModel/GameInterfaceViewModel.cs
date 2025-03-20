using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq; // For LINQ extension methods
using System.Runtime.CompilerServices;

namespace Cellular.ViewModel
{
    internal partial class GameInterfaceViewModel : INotifyPropertyChanged
    {
        private ObservableCollection<string> players;
        private ObservableCollection<string> arsenal;
        private ObservableCollection<Frame> frames;

        public ObservableCollection<string> Players
        {
            get => players;
            set
            {
                players = value;
                OnPropertyChanged();
            }
        }

        public ObservableCollection<string> Arsenal
        {
            get => arsenal;
            set
            {
                arsenal = value;
                OnPropertyChanged();
            }
        }

        public ObservableCollection<Frame> Frames
        {
            get => frames;
            set
            {
                frames = value;
                OnPropertyChanged();
            }
        }

        // Property to return the first 12 frames
        public ObservableCollection<Frame> First12Frames
        {
            get
            {
                // Returns the first 12 frames, or fewer if there are not enough frames
                return [.. frames.Take(12)];
            }
        }

        public GameInterfaceViewModel()
        {
            players =
            [
                "Zach",
                "Carson",
                "Thomas",
                "Josh"
            ];

            arsenal =
            [
                "Ball 1",
                "Ball 2",
                "Ball 3",
                "The Perfect Ball",
                "Ball 5",
                "Ball 6"
            ];

            frames =
            [
                new (1, 30),
                new (2, 60),
                new (3, 90),
                new (4, 90),
                new (5, 90),
                /*new (6, 90),
                new (7, 90),
                new (8, 90),
                new (9, 90),
                new (10, 90)*/
            ];
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

        // Constructor
        public Frame(int frameNumber, int rollingScore)
        {
            FrameNumber = frameNumber;
            RollingScore = rollingScore;
        }
    }

}
