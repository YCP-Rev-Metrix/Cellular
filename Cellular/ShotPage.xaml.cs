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
using System.Numerics;

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
            Debug.WriteLine("Date: " + viewModel.CurrentDate);
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
            if (viewModel.GameCompleted == true) return;
            if (sender is Button button && int.TryParse(button.Text, out int pinNumber) && pinNumber >= 1 && pinNumber <= 10)
            {
                int pinBit = 1 << (pinNumber - 1);

                if (viewModel.CurrentShot.Equals(1))
                {
                    // Toggle the bit for the selected pin
                    viewModel.pinStates ^= (short)pinBit;

                    // Update button color based on pin state
                    bool isPinDown = (viewModel.pinStates & pinBit) == 0;
                    button.BackgroundColor = isPinDown ? Colors.LightSlateGrey : Color.FromArgb("#9880e5");

                    Console.WriteLine($"Pin {pinNumber} is now {(isPinDown ? "Down" : "Up")}");
                }
                else
                {
                    // Only allow pins that were UP in shot 1 to be modified
                    bool wasUpInShot1 = (viewModel.shot1PinStates & pinBit) != 0;
                    bool wasFoul = (viewModel.shot1PinStates & (1 << 10)) != 0;


                    if (!wasUpInShot1 && !wasFoul)
                    {
                        Console.WriteLine($"Pin {pinNumber} was down in shot 1 and can't be changed in shot 2.");
                        return;
                    }

                    bool isCurrentlyDown = (viewModel.pinStates & pinBit) == 0;

                    if (isCurrentlyDown)
                    {
                        // Set the bit to 1 (mark as up)
                        viewModel.pinStates |= (short)pinBit;
                        button.BackgroundColor = Colors.Red;

                        Console.WriteLine($"Pin {pinNumber} is now Up");
                    }
                    else
                    {
                        // Set the bit to 0 (mark as down)
                        viewModel.pinStates &= (short)~pinBit;
                        button.BackgroundColor = Color.FromArgb("#9880e5");

                        Console.WriteLine($"Pin {pinNumber} is now Down");
                    }
                }

                UpdateShotBoxes();
            }
        }

        // Moves to the next shot or frame
        private async void OnNextClicked(object sender, EventArgs e)
        {
            var frameRepository = new FrameRepository(new CellularDatabase().GetConnection());
            await frameRepository.InitAsync();
            var currentFrame = viewModel.Frames.FirstOrDefault(f => f.FrameNumber == viewModel.CurrentFrame);
            var pins = new List<Button> { pin1, pin2, pin3, pin4, pin5, pin6, pin7, pin8, pin9, pin10 };
            bool foul = true;

            if (currentFrame == null || viewModel.GameCompleted == true || viewModel.CurrentFrame > 12) return;

            if (viewModel.CurrentShot.Equals(1))
            {
                if (currentFrame.ShotOneBox == "") return;
                else if (currentFrame.ShotOneBox != "F")
                {
                    viewModel.pinStates |= (short)(0 << 10); // Make sure the bit is saved as a 0 if foul was selected and no longer is
                    foul = false;
                }
                //Save shot to DB
                viewModel.firstShotId = await SaveShotAsync(1);

                ApplyPinColors(currentFrame);

                SetIsGameOver(); //Check if the game is over

                if (currentFrame.ShotOneBox == "X")
                {
                    //Save the frame to the database
                    await SaveFrameAsync(true, false);

                    if (viewModel.CurrentFrame == 10)
                    {
                        ShotPageFrame frame = new ShotPageFrame(11);
                        viewModel.Frames.Add(frame);
                    }
                    if (viewModel.CurrentFrame == 11)
                    {
                        ShotPageFrame frame = new ShotPageFrame(12);
                        viewModel.Frames.Add(frame);
                    }
                    if (viewModel.GameCompleted == false)
                    {
                        viewModel.CurrentFrame++;
                        viewModel.CurrentShot = 1;
                    }
                }
                else
                {
                    await SaveFrameAsync(false, false);
                    if (viewModel.GameCompleted == false)
                    {
                        viewModel.CurrentShot++;
                    }
                }
                if (foul == true && viewModel.CurrentShot == 1)
                {
                    foreach (var pin in pins)
                        pin.BackgroundColor = Color.FromArgb("#9880e5");
                }
                viewModel.shot1PinStates = viewModel.pinStates;
                viewModel.pinStates = 0;
            }
            else if (viewModel.CurrentShot.Equals(2))
            {
                if (currentFrame.ShotTwoBox == "") return;
                else if (currentFrame.ShotTwoBox != "F")
                {
                    viewModel.pinStates |= (short)(0 << 10); // Make sure the bit is saved as a 0 if foul was selected and no longer is
                    foul = false;
                }
                //Save shot to DB
                viewModel.secondShotId = await SaveShotAsync(2);
                Debug.WriteLine($"Second Shot Id: {viewModel.secondShotId}");

                ApplySecondShotColors(currentFrame);
                if (string.IsNullOrEmpty(currentFrame.ShotTwoBox))
                {
                    int downedPinsSecondShot = GetDownedPinsForShot(2) - int.Parse(currentFrame.ShotOneBox);
                    currentFrame.ShotTwoBox = downedPinsSecondShot.ToString();
                }
                int downedPins = GetDownedPinsTotalFrame(viewModel.pinStates);

                //Save the frame to the database and pass false for strike and true/false for spare
                if (downedPins == 10)
                {
                    await SaveFrameAsync(false, true);
                    Debug.WriteLine("Frame is saved as a spare");
                }
                else
                {
                    await SaveFrameAsync(false, false);
                    Debug.WriteLine("Frame is saved as neither");
                }
                if (currentFrame.ShotTwoBox == "/" && viewModel.CurrentFrame == 10)
                {
                    ShotPageFrame frame = new ShotPageFrame(11);
                    viewModel.Frames.Add(frame);
                }

                SetIsGameOver(); //Check if the game is over

                //Call these after saving the frame
                if (viewModel.GameCompleted != true)
                {
                    viewModel.CurrentFrame++;
                    viewModel.CurrentShot = 1;
                    viewModel.pinStates = 0;
                    viewModel.firstShotId = -1;
                    viewModel.secondShotId = -1;
                    viewModel.lastFrameId = -1;

                    foreach (var pin in pins)
                        pin.BackgroundColor = Colors.LightSlateGray;
                }
            }
            await UpdateScore();
            viewModel.OnPropertyChanged(nameof(viewModel.FrameDisplay));
            currentFrame.OnPropertyChanged(nameof(currentFrame.CenterPinColors));
            currentFrame.OnPropertyChanged(nameof(currentFrame.PinColors));
            viewModel.OnPropertyChanged(nameof(viewModel.Frames));
        }

        //Check if game is over
        private void SetIsGameOver()
        {
            var currentFrame = viewModel.Frames.FirstOrDefault(f => f.FrameNumber == viewModel.CurrentFrame);
            var lastFrame = viewModel.Frames.FirstOrDefault(f => f.FrameNumber == viewModel.CurrentFrame - 1);
            if (viewModel.CurrentFrame == 10)
            {
                if (currentFrame.ShotOneBox != "X")
                {
                    if (currentFrame.ShotTwoBox != "/" && viewModel.CurrentShot == 2)
                    {
                        viewModel.GameCompleted = true;
                        Debug.WriteLine("Game is now over");
                    }
                }
            }
            else if (viewModel.CurrentFrame == 11)
            {
                if (lastFrame.ShotTwoBox == "/" && viewModel.CurrentShot == 1)
                {
                    viewModel.GameCompleted = true;
                    Debug.WriteLine("Game is now over");
                }
                else if(viewModel.CurrentShot == 2)
                {
                    viewModel.GameCompleted = true;
                }
            }
            else if (viewModel.CurrentFrame == 12)
            {
                viewModel.GameCompleted = true;
                Debug.WriteLine("Game is now over");
            }
        }

        // Counts the number of downed pins based on pinStates
        private int GetDownedPinsForShot(int shotNumber)
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


        private int GetDownedPinsForFrameView(short previousState, short currentState)
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

        private int GetDownedPinsTotalFrame(short currentState)
        {
            int count = 0;
            for (int i = 0; i < 10; i++) // Only check first 10 bits
            {
                int currPin = (currentState >> i) & 1;

                if (currPin == 0) // Pin was up before and is now down
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

            bool isFoul = (viewModel.pinStates & (1 << 10)) != 0; // 11th bit check (bit index 10)
            int downedPins = GetDownedPinsForShot(1); // Assume this method gets the current number of pins knocked down

            if (viewModel.CurrentShot == 1)
            {
                if (isFoul)
                {
                    currentFrame.ShotOneBox = "F";
                }
                else if (downedPins == 0)
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
                isFoul = (viewModel.pinStates & (1 << 10)) != 0;
                bool wasShot1Foul = (viewModel.shot1PinStates & (1 << 10)) != 0;
                int newlyDownedPins = 0;

                // Count newly knocked-down pins by comparing shot1PinState and current pinState
                if (wasShot1Foul)
                {
                    newlyDownedPins = GetDownedPinsForShot(2);
                }
                else
                {
                    newlyDownedPins = GetDownedPinsForFrameView(viewModel.shot1PinStates, viewModel.pinStates);
                }

                if (isFoul)
                {
                    currentFrame.ShotTwoBox = "F";
                }
                else if (newlyDownedPins == 0)
                {
                    currentFrame.ShotTwoBox = "_";
                }
                else
                {
                    // Calculate how many pins were still standing before this shot
                    int shotOnePins = 10 - GetDownedPinsForFrameView(0b1111111111, viewModel.shot1PinStates);
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
                bool isStillStanding = (viewModel.pinStates & (1 << i)) != 0;

                if (isStillStanding)
                {
                    currentFrame.UpdatePinColor(i, Colors.Red); // Still standing, color it red
                }
            }
        }

        private void ReloadButtonColors()
        {
            var pins = new List<Button> { pin1, pin2, pin3, pin4, pin5, pin6, pin7, pin8, pin9, pin10 };

            for (int i = 0; i < 10; i++)
            {
                int pinBit = 1 << i;
                bool wasUpInShot1 = (viewModel.shot1PinStates & pinBit) != 0;

                if (wasUpInShot1)
                {
                    // Set the pin state to 0 (down)
                    viewModel.pinStates &= (short)~pinBit;

                    // Set button color to purple
                    pins[i].BackgroundColor = Color.FromArgb("#9880e5");
                }
            }
        }
        private void OnFoulClicked(object sender, EventArgs e)
        {
            var currentFrame = viewModel.Frames.FirstOrDefault(f => f.FrameNumber == viewModel.CurrentFrame);
            if (currentFrame == null || viewModel.GameCompleted == true) return;

            // Toggle the 11th bit (foul)
            viewModel.pinStates ^= (short)(1 << 10);
            bool isFoul = (viewModel.pinStates & (1 << 10)) != 0;

            // Update the shot box display
            string display = isFoul ? "F" : "";
            currentFrame.UpdateShotBox(viewModel.CurrentShot, display);
            viewModel.OnPropertyChanged(nameof(viewModel.Frames));
        }

        private void OnGutterClicked(object sender, EventArgs e)
        {
            var currentFrame = viewModel.Frames.FirstOrDefault(f => f.FrameNumber == viewModel.CurrentFrame);
            if (currentFrame == null || viewModel.GameCompleted == true) return;

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
                viewModel.pinStates = 0; // Reset pinStates for Shot 2

                for (int i = 0; i < pins.Count; i++)
                {
                    // If the pin was still standing after Shot 1
                    if ((viewModel.shot1PinStates & (1 << i)) != 0)
                    {
                        // Mark this pin as "missed" in shot 2
                        viewModel.pinStates |= (short)(1 << i);
                        pins[i].BackgroundColor = Colors.Red;
                    }
                    else
                    {
                        // Pin was already knocked down in shot 1 — keep gray
                        pins[i].BackgroundColor = Colors.LightSlateGrey;
                    }
                }
            }



            currentFrame.UpdateShotBox(viewModel.CurrentShot, "_");
            viewModel.OnPropertyChanged(nameof(viewModel.Frames));
        }


        private void OnSpareClicked(object sender, EventArgs e)
        {
            var currentFrame = viewModel.Frames.FirstOrDefault(f => f.FrameNumber == viewModel.CurrentFrame);
            if (currentFrame == null || viewModel.CurrentShot == 1 || viewModel.GameCompleted == true) return;

            currentFrame.UpdateShotBox(viewModel.CurrentShot, "/");
            viewModel.pinStates &= unchecked((short)~0x03FF); // All pins knocked down

            var pins = new List<Button> { pin1, pin2, pin3, pin4, pin5, pin6, pin7, pin8, pin9, pin10 };
            foreach (var pin in pins) pin.BackgroundColor = Colors.LightSlateGrey;

            viewModel.OnPropertyChanged(nameof(viewModel.pinStates));
        }

        private void OnStrikeClicked(object sender, EventArgs e)
        {
            var currentFrame = viewModel.Frames.FirstOrDefault(f => f.FrameNumber == viewModel.CurrentFrame);
            if (currentFrame == null || viewModel.CurrentShot == 2 || viewModel.GameCompleted == true) return;

            viewModel.pinStates &= unchecked((short)~0x03FF); // All pins knocked down
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
                Count = GetDownedPinsForShot(shotNumber),
                LeaveType = viewModel.pinStates,
                Side = null,
                Position = null,
                Frame = viewModel.CurrentFrame,
                Comment = null
            };

            Debug.WriteLine($"Saving Shot: Frame {newShot.Frame}, Shot {newShot.ShotNumber}, Pins Down {newShot.Count}");

            int shotId = await shotRepository.AddAsync(newShot);
            return shotId;
        }

        private async Task SaveFrameAsync(bool strike, bool spare)
        {
            // Initialize repositories
            var database = new CellularDatabase();
            var frameRepository = new FrameRepository(database.GetConnection());
            var gameRepository = new GameRepository(database.GetConnection());

            await frameRepository.InitAsync();
            await gameRepository.InitAsync();

            BowlingFrame newFrame;
            string result = null;

            if (strike)
            {
                result = "strike";
            } else if (spare) {
                result = "spare";
            }

            if (viewModel.CurrentShot.Equals(1))
            {
                Debug.WriteLine(Preferences.Get("GameID", 0));
                newFrame = new BowlingFrame
                {
                    FrameNumber = viewModel.CurrentFrame,
                    Lane = null,
                    Result = result,
                    GameId = Preferences.Get("GameID", 0),
                    Shot1 = viewModel.firstShotId
                };
                Debug.WriteLine($"Result is a {result}");
            }
            else
            {
                newFrame = await frameRepository.GetFrameById(viewModel.currentFrameId);
                if (!strike && viewModel.CurrentShot == 2)
                {
                    if (newFrame != null)
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
                viewModel.lastFrameId = newFrame.FrameId;
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

            for (int i = 0; i < frames.Count; i++)
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

                bool isShot1Foul = (shot1.LeaveType & (1 << 10)) != 0;
                bool isShot2Foul = shot2 != null && (shot2.LeaveType & (1 << 10)) != 0;

                // BLOCK Foul with 10 pins as "false strike"
                if (isShot1Foul && shot1.Count == 10)
                {
                    frameScore = 0;
                    totalScore += frameScore;

                    continue; // don't process it further
                }

                bool isStrike = !isShot1Foul && shot1.Count == 10;
                bool isSpare = !isStrike && shot2 != null && !isShot2Foul && (shot1.Count + shot2.Count == 10);

                if (isStrike)
                {
                    List<Shot> bonusShots = new();

                    for (int j = i + 1; j < frames.Count; j++)
                    {
                        var futureFrame = frames[j];

                        if (futureFrame.Shot1.HasValue)
                        {
                            var s1 = await shotRepository.GetShotById(futureFrame.Shot1.Value);
                            if (s1 != null)
                                bonusShots.Add(s1);
                        }

                        if (futureFrame.Shot2.HasValue)
                        {
                            var s2 = await shotRepository.GetShotById(futureFrame.Shot2.Value);
                            if (s2 != null)
                                bonusShots.Add(s2);
                        }

                        if (bonusShots.Count >= 2)
                            break;
                    }

                    // Only compute frameScore if we have enough bonus shots
                    if (bonusShots.Count >= 2)
                    {
                        Debug.WriteLine($"Bonus shot 1: {bonusShots[0].Count} and bonus shot 2: {bonusShots[1].Count}");
                        frameScore = 10 + bonusShots[0].Count + bonusShots[1].Count ?? 0;
                        totalScore += frameScore;

                        if (i < 10)
                        {
                            var uiFrame = viewModel.Frames[i];
                            uiFrame.RollingScore = totalScore;
                            uiFrame.OnPropertyChanged(nameof(uiFrame.RollingScore));
                        }
                    }
                    else
                    {
                        // Skip if not enough bonus shots to calculate score safely
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
                            frameScore = 10 + (bonus?.Count ?? 0);
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
                else if (!isSpare && shot2 != null)
                {

                    if (isShot1Foul)
                    {
                        frameScore = (shot2.Count ?? 0);
                    }
                    else if (isShot2Foul)
                    {
                        frameScore = (shot1.Count ?? 0);
                    }
                    else
                    {
                        frameScore = (shot1?.Count ?? 0) + (shot2?.Count ?? 0);
                    }
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

            for (int i = 0; i < frameIds.Count; i++) // Loop through all 10 frames
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
                            SetIsGameOver(); //Check if the game is over
                       
                            if (existingFrame.ShotOneBox.Equals("X"))
                            {
                                if (viewModel.CurrentFrame == 10)
                                {
                                    ShotPageFrame newFrame = new ShotPageFrame(11);
                                    viewModel.Frames.Add(newFrame);
                                }
                                if (viewModel.CurrentFrame == 11)
                                {
                                    ShotPageFrame newFrame = new ShotPageFrame(12);
                                    viewModel.Frames.Add(newFrame);
                                }
                                if (viewModel.GameCompleted != true && viewModel.CurrentFrame < viewModel.Frames.Count)
                                {
                                    viewModel.CurrentFrame++;
                                    viewModel.CurrentShot = 1;
                                }

                            }
                            else
                            {
                                if (viewModel.GameCompleted != true)
                                {
                                    viewModel.CurrentShot++;
                                }
                            }
                            ReloadButtonColors();
                            viewModel.shot1PinStates = viewModel.pinStates;
                            viewModel.pinStates &= unchecked((short)~0x03FF);
                        }
                        // Process shot 2
                        if (shot2 != null)
                        {
                            viewModel.pinStates = shot2?.LeaveType ?? 0;
                            ApplySecondShotColors(existingFrame);
                            UpdateShotBoxes();
                            SetIsGameOver(); //Check if the game is over

                            if (string.IsNullOrEmpty(existingFrame.ShotTwoBox))
                            {
                                int downedPinsSecondShot = GetDownedPinsForShot(2) - int.Parse(existingFrame.ShotOneBox);
                                existingFrame.ShotTwoBox = downedPinsSecondShot.ToString();
                            }

                            int downedPins = GetDownedPinsForFrameView(viewModel.shot1PinStates, viewModel.pinStates);

                            if (existingFrame.ShotTwoBox == "/" && viewModel.CurrentFrame == 10)
                            {
                                ShotPageFrame newFrame = new ShotPageFrame(11);
                                viewModel.Frames.Add(newFrame);
                            }

                            // Move to next frame and reset states
                            if (viewModel.GameCompleted != true)
                            {
                                viewModel.CurrentFrame++;
                                viewModel.CurrentShot = 1;
                                viewModel.pinStates &= unchecked((short)~0x03FF);
                                viewModel.firstShotId = -1;
                                viewModel.secondShotId = -1;
                                viewModel.lastFrameId = -1;

                                foreach (var pin in new List<Button> { pin1, pin2, pin3, pin4, pin5, pin6, pin7, pin8, pin9, pin10 })
                                    pin.BackgroundColor = Colors.LightSlateGray;
                            }
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
