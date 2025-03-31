using System;
using Microsoft.Maui.Storage;
using Cellular.ViewModel;
using System.Diagnostics;
using Cellular.Data;
using SQLitePCL;
using Microsoft.Maui.Controls;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel.Communication;

namespace Cellular
{
    public partial class ShotPage : ContentPage
    {
        private readonly GameInterfaceViewModel viewModel;

        public ShotPage()
        {
            InitializeComponent();
            viewModel = new GameInterfaceViewModel();
            BindingContext = viewModel;
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            //await viewModel.LoadGame();
            _ = viewModel.LoadUserHand();
            Debug.WriteLine("Hand: " + viewModel.Hand);
        }

        public void BoardChanged(object sender, EventArgs e)
        {
            if (sender is Slider slider)
            {
                double roundedValue = Math.Round(slider.Value);
                if (TestingLabel != null)
                {
                    int displayedValue = viewModel.Hand == "Left"
                        ? (roundedValue == 0 || roundedValue == 40 ? 0 : (int)roundedValue)
                        : (roundedValue == 0 || roundedValue == 40 ? 0 : 40 - (int)roundedValue);

                    TestingLabel.Text = displayedValue == 0 ? "Gutter" : displayedValue.ToString();
                }
            }
        }

        // Handles pin click events and toggles the pin state
        private void OnPinClicked(object sender, EventArgs e)
        {
            if (sender is Button button && int.TryParse(button.Text, out int pinNumber) && pinNumber >= 1 && pinNumber <= 10)
            {
                // Toggle the bit for the selected pin
                viewModel.pinStates ^= (short)(1 << (pinNumber - 1));

                // Update button color based on pin state
                bool isPinDown = (viewModel.pinStates & (1 << (pinNumber - 1))) == 0;
                button.BackgroundColor = isPinDown ? Colors.LightSlateGrey : Color.FromArgb("#9880e5");

                Console.WriteLine($"Pin {pinNumber} is now {(isPinDown ? "Down" : "Up")}");

                // Update shot boxes
                UpdateShotBoxes();
            }
        }

        // Moves to the next shot or frame
        private async void OnNextClicked(object sender, EventArgs e)
        {
            var currentFrame = viewModel.Frames.FirstOrDefault(f => f.FrameNumber == viewModel.CurrentFrame);
            var pins = new List<Button> { pin1, pin2, pin3, pin4, pin5, pin6, pin7, pin8, pin9, pin10 };

            if (currentFrame == null) return;

            if (viewModel.CurrentShot.Equals(1))
            {
                viewModel.firstShotId = await SaveShotAsync();
            }
            else
            {
                viewModel.secondShotId = await SaveShotAsync();
            }

            if (viewModel.CurrentShot == 1)

            {
                ApplyPinColors(currentFrame);
                if (currentFrame.ShotOneBox == "X")
                {
                    //Save the frame to the database
                    await SaveFrameAsync(true);


                    viewModel.CurrentFrame++;
                    viewModel.CurrentShot = 1;
                }
                else
                {
                    viewModel.CurrentShot++;
                }
                viewModel.shot1PinStates = viewModel.pinStates;
            }
            else if (viewModel.CurrentShot.Equals(2))
            {
                ApplySecondShotColors(currentFrame);
                if (string.IsNullOrEmpty(currentFrame.ShotTwoBox))
                {
                    int downedPinsSecondShot = GetDownedPins() - int.Parse(currentFrame.ShotOneBox);
                    currentFrame.ShotTwoBox = downedPinsSecondShot.ToString();
                }

                //Save the frame to the database
                await SaveFrameAsync(false);

                //Call these after saving the frame
                viewModel.CurrentFrame++;
                viewModel.CurrentShot = 1;
                viewModel.pinStates = 0;
                viewModel.firstShotId = -1;
                viewModel.secondShotId = -1;

                foreach (var pin in pins)
                    pin.BackgroundColor = Colors.LightSlateGray;
            }

            currentFrame.OnPropertyChanged(nameof(currentFrame.CenterPinColors));
            currentFrame.OnPropertyChanged(nameof(currentFrame.PinColors));
            viewModel.OnPropertyChanged(nameof(viewModel.Frames));
        }

        // Counts the number of downed pins based on pinStates
        private int GetDownedPins()
        {
            int count = 0;
            short pinStates = viewModel.pinStates;

            for (int i = 0; i < 10; i++)
            {
                if ((pinStates & (1 << i)) == 0) count++;
            }

            return count;
        }

        // Updates the shot boxes based on the number of downed pins
        private void UpdateShotBoxes()
        {
            var currentFrame = viewModel.Frames.FirstOrDefault(f => f.FrameNumber == viewModel.CurrentFrame);
            if (currentFrame == null) return;

            int downedPins = GetDownedPins();
            if(currentFrame.ShotOneBox == "_" || currentFrame.ShotTwoBox == "_")
            {
                return;
            }
            if (viewModel.CurrentShot == 1)
            {
                currentFrame.ShotOneBox = downedPins == 10 ? "X" : downedPins.ToString();
            }
            else if (viewModel.CurrentShot == 2)
            {
                int downedPinsSecondShot = downedPins - int.Parse(currentFrame.ShotOneBox);
                currentFrame.ShotTwoBox = downedPinsSecondShot.ToString();
            }

            viewModel.OnPropertyChanged(nameof(viewModel.Frames));
        }

        private void ApplyPinColors(ShotPageFrame currentFrame)
        {
            for (int i = 0; i < 10; i++)
            {
                bool isPinDown = (viewModel.pinStates & (1 << i)) == 0;
                currentFrame.UpdatePinColor(i, isPinDown ? Colors.LightGray : Colors.Black);
            }
        }

        private void ApplySecondShotColors(ShotPageFrame currentFrame)
        {
            for (int i = 0; i < 10; i++)
            {
                bool wasStanding = (viewModel.shot1PinStates & (1 << i)) != 0;
                bool isNowDown = (viewModel.pinStates & (1 << i)) == 0;

                if (wasStanding && isNowDown)
                {
                    currentFrame.UpdateCenterPinColor(i, Colors.White);
                }
                else if (wasStanding)
                {
                    currentFrame.UpdatePinColor(i, Colors.Red);
                }
            }
        }

        private void OnFoulClicked(object sender, EventArgs e)
        {
            var currentFrame = viewModel.Frames.FirstOrDefault(f => f.FrameNumber == viewModel.CurrentFrame);
            if (currentFrame == null) return;

            currentFrame.UpdateShotBox(viewModel.CurrentShot, "F");
            viewModel.OnPropertyChanged(nameof(viewModel.Frames));
        }

        private void OnGutterClicked(object sender, EventArgs e)
        {
            var currentFrame = viewModel.Frames.FirstOrDefault(f => f.FrameNumber == viewModel.CurrentFrame);
            if (currentFrame == null) return;

            var pins = new List<Button> { pin1, pin2, pin3, pin4, pin5, pin6, pin7, pin8, pin9, pin10 };

            if (viewModel.CurrentShot.Equals(1))
            {
                viewModel.pinStates = 1;
                for (int i = 0; i < 10; i++)
                {
                    viewModel.pinStates |= (short)(1 << i); // Set each bit from 0 to 9 to 1
                }
                foreach (var pin in pins)
                    pin.BackgroundColor = Color.FromArgb("#9880e5");
            }
            else
            {

                for (int i = 0; i < pins.Count; i++)
                {
                    if ((viewModel.shot1PinStates & (1 << i)) == 0) // If the pin was knocked down in shot 1
                    {
                        pins[i].BackgroundColor = Colors.LightSlateGrey;
                    }
                    else
                    {
                        pins[i].BackgroundColor = Color.FromArgb("#9880e5");
                    }
                }
            }

            currentFrame.UpdateShotBox(viewModel.CurrentShot, "_");
            viewModel.OnPropertyChanged(nameof(viewModel.Frames));
        }


        private void OnSpareClicked(object sender, EventArgs e)
        {
            var currentFrame = viewModel.Frames.FirstOrDefault(f => f.FrameNumber == viewModel.CurrentFrame);
            if (currentFrame == null || viewModel.CurrentShot == 1) return;

            currentFrame.UpdateShotBox(viewModel.CurrentShot, "/");
            viewModel.pinStates = 0; // All pins knocked down

            var pins = new List<Button> { pin1, pin2, pin3, pin4, pin5, pin6, pin7, pin8, pin9, pin10 };
            foreach (var pin in pins) pin.BackgroundColor = Colors.LightSlateGrey;

            viewModel.OnPropertyChanged(nameof(viewModel.pinStates));
        }

        private void OnStrikeClicked(object sender, EventArgs e)
        {
            var currentFrame = viewModel.Frames.FirstOrDefault(f => f.FrameNumber == viewModel.CurrentFrame);
            if (currentFrame == null || viewModel.CurrentShot == 2) return;

            viewModel.pinStates = 0; // All pins knocked down
            currentFrame.UpdateShotBox(viewModel.CurrentShot, "X");

            var pins = new List<Button> { pin1, pin2, pin3, pin4, pin5, pin6, pin7, pin8, pin9, pin10 };
            foreach (var pin in pins) pin.BackgroundColor = Colors.LightSlateGrey;

            viewModel.OnPropertyChanged(nameof(viewModel.Frames));
        }

        private async Task<int> SaveShotAsync()
        {
            var shotRepository = new ShotRepository(new CellularDatabase().GetConnection());
            await shotRepository.InitAsync();

            var newShot = new Shot
            {
                UserId = Preferences.Get("UserId", 0),
                ShotNumber = viewModel.CurrentShot,
                Ball = null,
                Count = GetDownedPins(),
                LeaveType = viewModel.pinStates,
                Side = null,
                Position = null,
                Frame = viewModel.CurrentFrame,
                Game = viewModel.currentGame
            };

            Debug.WriteLine($"Saving Shot: Frame {newShot.Frame}, Shot {newShot.ShotNumber}, Pins Down {newShot.Count}");

            int shotId = await shotRepository.AddAsync(newShot);
            return shotId;
        }

        private async Task SaveFrameAsync(bool strike)
        {
            // Initialize repositories
            var database = new CellularDatabase();
            var frameRepository = new FrameRepository(database.GetConnection());
            var gameRepository = new GameRepository(database.GetConnection());

            await frameRepository.InitAsync();
            await gameRepository.InitAsync();

            // Create new frame object
            var newFrame = new BowlingFrame
            {
                UserId = Preferences.Get("UserId", 0),
                FrameNumber = viewModel.CurrentFrame,
                Lane = null,
                Result = null,
                Shots = viewModel.firstShotId.ToString()
            };

            if (!strike)
            {
                newFrame.Shots += "_" + viewModel.secondShotId;
            }

            Debug.WriteLine($"Saving Frame: Frame Number {newFrame.FrameNumber}, ShotId's {newFrame.Shots}");

            // Save the frame
            await frameRepository.AddFrame(newFrame);

            // Retrieve the inserted frame ID
            newFrame.FrameId = (await database.GetConnection().Table<BowlingFrame>()
                                         .OrderByDescending(f => f.FrameId)
                                         .FirstOrDefaultAsync())?.FrameId ?? 0;

            viewModel.currentFrameId = newFrame.FrameId;

            // Retrieve the current game
            var userId = Preferences.Get("UserId", 0);
            var gameUpdate = await gameRepository.GetGameBySessionAndGameNumberAsync(viewModel.currentSession, viewModel.currentGame, userId);

            if (gameUpdate != null)
            {
                // Ensure Frames string is formatted properly with underscore separation
                gameUpdate.Frames = string.IsNullOrEmpty(gameUpdate.Frames)
                    ? $"{viewModel.currentFrameId}_"
                    : $"{gameUpdate.Frames}{viewModel.currentFrameId}_";

                Debug.WriteLine($"Saving frame to game: frames by id: {gameUpdate.Frames}");

                // Update game in the database
                await gameRepository.UpdateGameAsync(gameUpdate);
            }
            else
            {
                Debug.WriteLine("Error: Could not find game to update.");
            }
        }


    }
}
