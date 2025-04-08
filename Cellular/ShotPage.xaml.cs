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
using System.Collections.ObjectModel;

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
            _ = CheckIfFramesExistForGame();
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
            if (viewModel.CurrentFrame > 12) return;

            if (viewModel.CurrentShot.Equals(1))
            {
                //Save shot to DB
                viewModel.firstShotId = await SaveShotAsync(1);

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
                    await SaveFrameAsync(false);
                    viewModel.CurrentShot++;
                }
                viewModel.shot1PinStates = viewModel.pinStates;
            }
            else if (viewModel.CurrentShot.Equals(2))
            {
                //Save shot to DB
                viewModel.secondShotId = await SaveShotAsync(2);
                Debug.WriteLine($"Second Shot Id: {viewModel.secondShotId}");

                ApplySecondShotColors(currentFrame);
                if (string.IsNullOrEmpty(currentFrame.ShotTwoBox))
                {
                    int downedPinsSecondShot = GetDownedPins(2) - int.Parse(currentFrame.ShotOneBox);
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

            await UpdateScore();
            currentFrame.OnPropertyChanged(nameof(currentFrame.CenterPinColors));
            currentFrame.OnPropertyChanged(nameof(currentFrame.PinColors));
            viewModel.OnPropertyChanged(nameof(viewModel.Frames));
        }

        // Counts the number of downed pins based on pinStates
        private int GetDownedPins(int shotNumber)
        {
            int count = 0;
            short pinStates = viewModel.pinStates;
            short shot1PinStates = viewModel.shot1PinStates;

            if (shotNumber == 1)
            {
                // Count pins that are down (bit is 0)
                for (int i = 0; i < 10; i++)
                {
                    if ((pinStates & (1 << i)) == 0) count++;
                }
            }
            else
            {
                // Count newly downed pins: were up in shot1, but now down
                for (int i = 0; i < 10; i++)
                {
                    bool wasStanding = (shot1PinStates & (1 << i)) != 0;
                    bool isNowDown = (pinStates & (1 << i)) == 0;

                    if (wasStanding && isNowDown)
                    {
                        count++;
                    }
                }
            }

            return count;
        }


        private int CountKnockedDownPins(short previousState, short currentState)
        {
            int count = 0;
            for (int i = 0; i < 10; i++) // Only check first 10 bits
            {
                int prevPin = (previousState >> i) & 1;
                int currPin = (currentState >> i) & 1;

                if (prevPin == 1 && currPin == 0) // Pin was up before and is now down
                {
                    count++;
                }
            }
            return count;
        }

        // Updates the shot boxes based on the number of downed pins
        private void UpdateShotBoxes()
        {
            var currentFrame = viewModel.Frames.FirstOrDefault(f => f.FrameNumber == viewModel.CurrentFrame);
            if (currentFrame == null) return;

            int downedPins = GetDownedPins(1); // Assume this method gets the current number of pins knocked down

            if (viewModel.CurrentShot == 1)
            {
                if (downedPins == 0)
                {
                    // If no pins were knocked down, set ShotOneBox to "_"
                    currentFrame.ShotOneBox = "_";
                }
                else
                {
                    // If 10 pins are knocked down on the first shot, it's a strike (represented by "X")
                    currentFrame.ShotOneBox = downedPins == 10 ? "X" : downedPins.ToString();
                }
            }
            else if (viewModel.CurrentShot == 2)
            {
                // Count newly knocked-down pins by comparing shot1PinState and current pinState
                int newlyDownedPins = CountKnockedDownPins(viewModel.shot1PinStates, viewModel.pinStates);

                if (newlyDownedPins == 0)
                {
                    currentFrame.ShotTwoBox = "_";
                }
                else
                {
                    // Calculate how many pins were still standing before this shot
                    int shotOnePins = 10 - CountKnockedDownPins(0b1111111111, viewModel.shot1PinStates);
                    int remainingPins = 10 - shotOnePins;

                    // Display "/" if all remaining pins were knocked down, otherwise show newly downed pins
                    currentFrame.ShotTwoBox = (downedPins == 10) ? "/" : newlyDownedPins.ToString();
                }
            }

            // Notify property change
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

        private async Task<int> SaveShotAsync(int shotNumber)
        {
            var shotRepository = new ShotRepository(new CellularDatabase().GetConnection());
            await shotRepository.InitAsync();

            var newShot = new Shot
            {
                ShotNumber = viewModel.CurrentShot,
                Ball = null,
                Count = GetDownedPins(shotNumber),
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

            BowlingFrame newFrame;

            if (viewModel.CurrentShot.Equals(1))
            {
                Debug.WriteLine(Preferences.Get("GameID", 0));
                newFrame = new BowlingFrame
                {
                    FrameNumber = viewModel.CurrentFrame,
                    Lane = null,
                    Result = null,
                    GameId = Preferences.Get("GameID", 0),
                    Shot1 = viewModel.firstShotId
                };
            }
            else
            {
                newFrame = await frameRepository.GetFrameById(viewModel.currentFrameId);
                if (!strike && viewModel.CurrentShot == 2)
                {
                    if(newFrame != null)
                    {
                        Debug.WriteLine("NOT NULL");
                        newFrame.Shot2 = viewModel.secondShotId;
                    }
                }
            }

            if (viewModel.CurrentShot.Equals(1))
            {
                await frameRepository.AddFrame(newFrame);
                Debug.WriteLine("Frame added to db");
            }
            else
            {
                await frameRepository.UpdateFrameAsync(newFrame);
                Debug.WriteLine($"Frame updated {newFrame.Shot1} and {newFrame.Shot2}");
            }

            // Retrieve the inserted frame ID
            newFrame.FrameId = (await database.GetConnection().Table<BowlingFrame>()
                                         .OrderByDescending(f => f.FrameId)
                                         .FirstOrDefaultAsync())?.FrameId ?? 0;

            viewModel.currentFrameId = newFrame.FrameId;
        }

        public async Task UpdateScore()
        {
            var db = new CellularDatabase().GetConnection();
            var frameRepository = new FrameRepository(db);
            var shotRepository = new ShotRepository(db);

            await frameRepository.InitAsync();
            await shotRepository.InitAsync();

            var frameIds = await frameRepository.GetFrameIdsByGameIdAsync(Preferences.Get("GameID", 0));

            List<BowlingFrame> frames = new();
            foreach (int id in frameIds)
            {
                var frame = await frameRepository.GetFrameById(id);
                if (frame != null)
                {
                    frames.Add(frame);
                }
            }

            int totalScore = 0;

            for (int i = 0; i < frames.Count && i < 10; i++)
            {
                var frame = frames[i];
                int frameScore = 0;

                Shot shot1 = null;
                Shot shot2 = null;

                if (frame.Shot1.HasValue)
                    shot1 = await shotRepository.GetShotById(frame.Shot1.Value);

                if (frame.Shot2.HasValue)
                    shot2 = await shotRepository.GetShotById(frame.Shot2.Value);

                if (shot1 == null)
                    continue;

                bool isStrike = shot1.Count == 10;
                bool isSpare = !isStrike && shot2 != null && (shot1.Count + shot2.Count == 10);

                if (isStrike)
                {
                    List<Shot> bonusShots = new();
                    bool hasNonStrike = false;

                    // Search all future frames for 2 valid shots
                    for (int j = i + 1; j < frames.Count; j++)
                    {
                        var futureFrame = frames[j];

                        if (futureFrame.Shot1.HasValue)
                        {
                            var s1 = await shotRepository.GetShotById(futureFrame.Shot1.Value);
                            if (s1 != null)
                            {
                                bonusShots.Add(s1);
                                if (s1.Count < 10) hasNonStrike = true;
                            }
                        }

                        if (futureFrame.Shot2.HasValue)
                        {
                            var s2 = await shotRepository.GetShotById(futureFrame.Shot2.Value);
                            if (s2 != null)
                            {
                                bonusShots.Add(s2);
                                if (s2.Count < 10) hasNonStrike = true;
                            }
                        }

                        // Only exit loop once we have 2 shots AND at least one non-strike
                        if (bonusShots.Count >= 2 && hasNonStrike)
                            break;
                    }

                    bool gameComplete = frames.Count >= 10 && i >= 9 && bonusShots.Count == 2;

                    if ((bonusShots.Count >= 2 && hasNonStrike) || gameComplete)
                    {
                        frameScore = 10 + bonusShots[0].Count + bonusShots[1].Count?? 0;
                        totalScore += frameScore;

                        if (i < viewModel.Frames.Count)
                        {
                            var uiFrame = viewModel.Frames[i];
                            uiFrame.RollingScore = totalScore;
                            uiFrame.OnPropertyChanged(nameof(uiFrame.RollingScore));
                        }
                    }
                    else
                    {
                        // Not enough data yet — skip this frame
                        continue;
                    }
                }

                else if (isSpare)
                {
                    if (i + 1 < frames.Count)
                    {
                        var nextFrame = frames[i + 1];
                        Shot bonus = null;

                        if (nextFrame.Shot1.HasValue)
                        {
                            bonus = await shotRepository.GetShotById(nextFrame.Shot1.Value);
                        }

                        if (bonus != null)
                        {
                            frameScore = 10 + bonus.Count ?? 0;
                            totalScore += frameScore;

                            if (i < viewModel.Frames.Count)
                            {
                                var uiFrame = viewModel.Frames[i];
                                uiFrame.RollingScore = totalScore;
                                uiFrame.OnPropertyChanged(nameof(uiFrame.RollingScore));
                            }
                        }
                        else
                        {
                            continue;
                        }
                    }
                }
                else if (shot2 != null)
                {
                    frameScore = shot1.Count + shot2.Count ?? 0;
                    totalScore += frameScore;

                    if (i < viewModel.Frames.Count)
                    {
                        var uiFrame = viewModel.Frames[i];
                        uiFrame.RollingScore = totalScore;
                        uiFrame.OnPropertyChanged(nameof(uiFrame.RollingScore));
                    }
                }
            }

            viewModel.OnPropertyChanged(nameof(viewModel.Frames));
        }


        public async Task CheckIfFramesExistForGame()
        {
            var frameRepository = new FrameRepository(new CellularDatabase().GetConnection());
            //Debug.WriteLine("-------------------------------------------------------------------------*");
            //Debug.WriteLine($"Game Id: "+Preferences.Get("GameID", 0));
            var existingFrameIds = await frameRepository.GetFrameIdsByGameIdAsync(Preferences.Get("GameID", 0));
            //Debug.WriteLine("-------------------------------------------------------------------------");

            if (!existingFrameIds.Any())
            {
                Debug.WriteLine("No frames found.");
                Debug.WriteLine($" Session: {viewModel.currentSession}, Game: {viewModel.currentGame}");
            }
            else
            {
                Debug.WriteLine("Frames found.");
                Debug.WriteLine($" Session: {viewModel.currentSession}, Game: {viewModel.currentGame}");
                await LoadExistingGameData();
            }
        }

        private async Task LoadExistingGameData()
        {
            var db = new CellularDatabase().GetConnection();
            var gameRepository = new GameRepository(db);
            var frameRepository = new FrameRepository(db);
            var shotRepository = new ShotRepository(db);

            await gameRepository.InitAsync();
            await frameRepository.InitAsync();
            await shotRepository.InitAsync();

            // Retrieve the current game
            var game = await gameRepository.GetGame(viewModel.currentSession, viewModel.currentGame, viewModel.UserId);
            List<int> frameIds = await frameRepository.GetFrameIdsByGameIdAsync(Preferences.Get("GameID", 0));

            Debug.WriteLine($"Frame Ids to load: {string.Join(", ", frameIds)}");

            // Ensure there are exactly 10 frames in viewModel.Frames
            while (viewModel.Frames.Count < 10)
            {
                viewModel.Frames.Add(new ShotPageFrame(viewModel.Frames.Count + 1));
            }

            for (int i = 0; i < 10; i++) // Loop through all 10 frames
            {
                var frame = i < frameIds.Count ? await frameRepository.GetFrameById(frameIds[i]) : null;
                var existingFrame = viewModel.Frames[i];

                if (frame != null)
                {
                    existingFrame.FrameNumber = frame.FrameNumber ?? (i + 1);
                    existingFrame.ShotOneBox = "";
                    existingFrame.ShotTwoBox = "";

                    if (!(frame.Shot1==null))
                    {
                        Debug.WriteLine($"Frame {frame.FrameNumber}, Shots: {frame.Shot1} , {frame.Shot2}");
                        List<int> shotIds = await frameRepository.GetShotIdsByFrameIdAsync(frameIds[i]);

                        Debug.WriteLine($"Shot Ids to load: {string.Join(", ", shotIds)}");

                        viewModel.currentFrameId = frame.FrameId;

                        Shot shot1 = null, shot2 = null;

                        if (shotIds.Count > 0)
                        {
                            shot1 = await shotRepository.GetShotById(shotIds[0]);
                            Debug.WriteLine($"Shot1 count: {shot1?.Count}");
                        }

                        if (shotIds.Count > 1)
                        {
                            shot2 = await shotRepository.GetShotById(shotIds[1]);
                            Debug.WriteLine($"Shot2 count: {shot2?.Count}");
                        }

                        // Process shot 1
                        if (shot1 != null)
                        {
                            viewModel.pinStates = shot1?.LeaveType ?? 0;
                            UpdateShotBoxes();
                            ApplyPinColors(existingFrame);

                            if (existingFrame.ShotOneBox.Equals("X"))
                            {
                                // Update frame for strike
                                viewModel.CurrentFrame++;
                                viewModel.CurrentShot = 1;
                            }
                            else if(shot1.Count == 0)
                            {
                                var pins = new List<Button> { pin1, pin2, pin3, pin4, pin5, pin6, pin7, pin8, pin9, pin10 };
                                foreach (var pin in pins) pin.BackgroundColor = Color.FromArgb("#9880e5");
                                viewModel.CurrentShot++;
                            }
                            else
                            {
                                viewModel.CurrentShot++;
                            }

                            viewModel.shot1PinStates = shot1?.LeaveType ?? 0;
                        }

                        // Process shot 2
                        if (shot2 != null)
                        {
                            viewModel.pinStates = shot2?.LeaveType ?? 0;
                            ApplySecondShotColors(existingFrame);
                            UpdateShotBoxes();

                            if (string.IsNullOrEmpty(existingFrame.ShotTwoBox))
                            {
                                int downedPinsSecondShot = GetDownedPins(2) - int.Parse(existingFrame.ShotOneBox);
                                existingFrame.ShotTwoBox = downedPinsSecondShot.ToString();
                            }

                            // Move to next frame and reset states
                            viewModel.CurrentFrame++;
                            viewModel.CurrentShot = 1;
                            viewModel.pinStates = 0;
                            viewModel.firstShotId = -1;
                            viewModel.secondShotId = -1;

                            foreach (var pin in new List<Button> { pin1, pin2, pin3, pin4, pin5, pin6, pin7, pin8, pin9, pin10 })
                                pin.BackgroundColor = Colors.LightSlateGray;
                        }
                    }

                    // Notify the UI of changes
                    existingFrame.OnPropertyChanged(nameof(existingFrame.CenterPinColors));
                    existingFrame.OnPropertyChanged(nameof(existingFrame.PinColors));
                }
                else
                {
                    // Reset empty frames (if they exist in UI but not in DB)
                    existingFrame.FrameNumber = i + 1;
                    existingFrame.ShotOneBox = "";
                    existingFrame.ShotTwoBox = "";
                }
            }

            await UpdateScore();
            // Notify the viewModel of frame updates
            viewModel.OnPropertyChanged(nameof(viewModel.Frames));
        }
    }
}
