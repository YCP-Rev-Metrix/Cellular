using Cellular.Data;
using Cellular.ViewModel;
using Cellular.Views;
using CellularCore;
using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Extensions;
using Microsoft.Maui.ApplicationModel.Communication;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using SQLitePCL;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

namespace Cellular
{
    public partial class ShotPage : ContentPage
    {
        private readonly GameInterfaceViewModel viewModel;

        private bool _hasAppeared = false;
        public ShotPage()
        {
            InitializeComponent();
            viewModel = new GameInterfaceViewModel();
            BindingContext = viewModel;
            viewModel.AlertEditFrame += HandleEditFrame;
        }

        protected override void OnAppearing()
        {
            if (!_hasAppeared)
            {
                _hasAppeared = true;
                _ = CheckIfFramesExistForGame();
                _ = viewModel.LoadUserHand();
                Debug.WriteLine("Hand: " + viewModel.Hand);
                Debug.WriteLine("Date: " + viewModel.CurrentDate);
            }
            else
            {
                // If you need to refresh only specific UI elements when returning from a popup,
                // do it here in a minimal way rather than re-running full initialization.
                Debug.WriteLine("ShotPage re-appeared (popup closed or focus returned). Skipping full reload.");
            }
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
                if (currentFrame.ShotOneBox == "X")
                {
                    viewModel.frameResult = "Strike";
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
                    viewModel.frameResult = "Spare";
                    await SaveFrameAsync(false, true);
                    Debug.WriteLine("Frame is saved as a spare");
                }
                else
                {
                    viewModel.frameResult = null;
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
                    viewModel.frameResult = null;
                    viewModel.pinStates = 0;
                    viewModel.firstShotId = -1;
                    viewModel.secondShotId = -1;
                    viewModel.lastFrameId = -1;

                    foreach (var pin in pins)
                        pin.BackgroundColor = Colors.LightSlateGray;

                    if (viewModel.EditMode)
                    {
                        viewModel.EditMode = false;
                        await LoadExistingGameData();
                    }
                }
            }
            viewModel.Comment = "";
            await UpdateScore();
            viewModel.OnPropertyChanged(nameof(viewModel.FrameDisplay));
            currentFrame.OnPropertyChanged(nameof(currentFrame.CenterPinColors));
            currentFrame.OnPropertyChanged(nameof(currentFrame.PinColors));
            viewModel.OnPropertyChanged(nameof(viewModel.Frames));
        }

        private async void OnCommentClicked(Object sender, EventArgs e)
        {
            string initial = viewModel.Comment ?? "";
            var popup = new CommentEditorPopup(initial);

            // Ensure Completion is set if the popup is closed by other means.
            popup.Closed += (s, args) =>
            {
                if (!popup.Completion.Task.IsCompleted)
                {
                    popup.Completion.TrySetResult(null);
                }
            };

            this.ShowPopup(popup);

            // Await the completion source that the popup sets on save/cancel/close
            var result = await popup.Completion.Task;

            // Only update viewModel when the user explicitly saved (non-null result)
            if (result is string text && text != null)
            {
                viewModel.Comment = text;
                viewModel.OnPropertyChanged(nameof(viewModel.Comment));
                Debug.WriteLine($"Comment saved: {viewModel.Comment}");
            }
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
            string result = Convert.ToString((ushort)viewModel.pinStates, 2).PadLeft(16, '0');
            Debug.WriteLine($"Shot {shotNumber}:{result}");
            // Delegate to ShotCalculator to make logic testable
            return ShotCalculator.GetDownedPinsForShot(viewModel.pinStates, viewModel.shot1PinStates, shotNumber);
        }

        private int GetDownedPinsForFrameView(short previousState, short currentState)
        {
            // Delegate to ShotCalculator
            return ShotCalculator.GetDownedPinsForFrameView(previousState, currentState);
        }

        private int GetDownedPinsTotalFrame(short currentState)
        {
            // Delegate to ShotCalculator
            return ShotCalculator.GetDownedPinsTotalFrame(currentState);
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
                    // If no pins were knocked down, set ShotOneBox to "-"
                    currentFrame.ShotOneBox = "-";
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
                    currentFrame.ShotTwoBox = "-";
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

        private void ReloadButtonColors(bool editMode = false)
        {
            var pins = new List<Button> { pin1, pin2, pin3, pin4, pin5, pin6, pin7, pin8, pin9, pin10 };

            for (int i = 0; i < 10; i++)
            {
                int pinBit = 1 << i;
                bool wasUpInShot1 = (viewModel.shot1PinStates & pinBit) != 0;

                if (wasUpInShot1)
                {
                    if (!editMode)
                    {
                        // Set the pin state to 0 (down)
                        viewModel.pinStates &= (short)~pinBit;
                    }

                    // Set button color to purple
                    pins[i].BackgroundColor = Color.FromArgb("#9880e5");
                }
                else
                {
                    pins[i].BackgroundColor = Colors.LightSlateGray;
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

            currentFrame.UpdateShotBox(viewModel.CurrentShot, "-");
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

        private async Task<int> UpdateGameAsync()
        {
            var database = new CellularDatabase();
            var conn = database.GetConnection();
            var gameRepository = new GameRepository(conn);
            await gameRepository.InitAsync();
            var game = await gameRepository.GetGameById(viewModel.gameId);
            if (game != null)
            {
                game.Score = viewModel.TotalScore;
                await gameRepository.UpdateGameAsync(game);
                Debug.WriteLine($"Game {game.GameNumber} score updated to {game.Score}");
                return game.Score ?? 0;
            }
            else
            {
                Debug.WriteLine($"UpdateGameAsync: No game found with GameId {viewModel.gameId}");
                return 0;
            }
        }

        private async Task<int> SaveShotAsync(int shotNumber)
        {
            var database = new CellularDatabase();
            var conn = database.GetConnection();
            var shotRepository = new ShotRepository(conn);
            var frameRepository = new FrameRepository(conn);
            await shotRepository.InitAsync();
            await frameRepository.InitAsync();

            // Use same canonical lookup as DoesShotExistAsync (GameId + FrameNumber)
            var reloadFrame = await conn.Table<BowlingFrame>()
                                .Where(f => f.GameId == viewModel.gameId && f.FrameNumber == viewModel.CurrentFrame)
                                .FirstOrDefaultAsync();

            bool shotExists = await frameRepository.DoesShotExistAsync(viewModel.gameId, viewModel.CurrentFrame, shotNumber);
            int shotId = -1;

            int currentBall = -1;
            if(shotNumber == 1)
            {
                currentBall = viewModel.StrikeBallId;
            }
            else if(shotNumber == 2)
            {
                currentBall = viewModel.secondShotId;
            }

            if (!shotExists)
            {
                // Create and save new shot
                var newShot = new Shot
                {
                    ShotNumber = viewModel.CurrentShot,
                    Ball = currentBall,
                    Count = GetDownedPinsForShot(shotNumber),
                    LeaveType = viewModel.pinStates,
                    Side = null,
                    Position = null,
                    Frame = viewModel.CurrentFrame,
                    Comment = viewModel.Comment
                };

                Debug.WriteLine($"Saving Shot: Frame {newShot.Frame}, Shot {newShot.ShotNumber}, Pins Down {newShot.Count}");
                shotId = await shotRepository.AddAsync(newShot);

                // Ensure frame exists and references this shot
                if (reloadFrame == null)
                {
                    // Create a new frame record and set the proper shot field
                    var newFrame = new BowlingFrame
                    {
                        FrameNumber = viewModel.CurrentFrame,
                        Lane = null,
                        Result = viewModel.frameResult,
                        GameId = viewModel.gameId,
                        Shot1 = shotNumber == 1 ? shotId : (int?)null,
                        Shot2 = shotNumber == 2 ? shotId : (int?)null
                    };
                    await frameRepository.AddFrame(newFrame);
                    Debug.WriteLine($"Result3: {viewModel.frameResult}");

                    // update viewModel.currentFrameId to the newly inserted frame
                    newFrame.FrameId = (await conn.Table<BowlingFrame>().OrderByDescending(f => f.FrameId).FirstOrDefaultAsync())?.FrameId ?? 0;
                    viewModel.currentFrameId = newFrame.FrameId;
                }
                else
                {
                    // Update the existing frame to reference the new shot id
                    if (shotNumber == 1) reloadFrame.Shot1 = shotId;
                    else reloadFrame.Shot2 = shotId;

                    await frameRepository.UpdateFrameAsync(reloadFrame);
                    viewModel.currentFrameId = reloadFrame.FrameId;
                }
            }
            else
            {
                // Shot exists: update existing shot record
                if (reloadFrame == null)
                {
                    Debug.WriteLine($"SaveShotAsync: expected frame for GameId {viewModel.gameId}, FrameNumber {viewModel.CurrentFrame} but none found.");
                    // Attempt to fall back to using viewModel.currentFrameId if set
                    if (viewModel.currentFrameId > 0)
                        reloadFrame = await frameRepository.GetFrameById(viewModel.currentFrameId);
                }

                if (shotNumber == 1)
                {
                    if (reloadFrame?.Shot1 != null)
                    {
                        var reloadShotOne = await shotRepository.GetShotById(reloadFrame.Shot1.Value);
                        if (reloadShotOne != null)
                        {
                            Debug.WriteLine("Updating shot 1");
                            shotId = reloadShotOne.ShotId;
                            reloadShotOne.Ball = viewModel.StrikeBallId;
                            reloadShotOne.LeaveType = viewModel.pinStates;
                            reloadShotOne.Count = GetDownedPinsForShot(1);
                            reloadShotOne.Comment = viewModel.Comment;
                            await shotRepository.UpdateShotAsync(reloadShotOne);
                        }
                    }
                    else
                    {
                        Debug.WriteLine("SaveShotAsync: no Shot1 id on frame; creating new Shot and linking it.");
                        var newShot = new Shot
                        {
                            ShotNumber = 1,
                            Ball = viewModel.StrikeBallId,
                            Count = GetDownedPinsForShot(1),
                            LeaveType = viewModel.pinStates,
                            Frame = viewModel.CurrentFrame,
                            Comment = viewModel.Comment
                        };
                        shotId = await shotRepository.AddAsync(newShot);
                        if (reloadFrame != null)
                        {
                            reloadFrame.Shot1 = shotId;
                            await frameRepository.UpdateFrameAsync(reloadFrame);
                        }
                    }
                }
                else // shotNumber == 2
                {
                    if (reloadFrame?.Shot2 != null)
                    {
                        var reloadShotTwo = await shotRepository.GetShotById(reloadFrame.Shot2.Value);
                        if (reloadShotTwo != null)
                        {
                            Debug.WriteLine("Updating shot 2");
                            shotId = reloadShotTwo.ShotId;
                            reloadShotTwo.LeaveType = viewModel.pinStates;
                            string result = Convert.ToString((ushort)viewModel.pinStates, 2).PadLeft(16, '0');
                            Debug.WriteLine($"{result}");
                            int down = GetDownedPinsForShot(2);
                            reloadShotTwo.Count = down;
                            await shotRepository.UpdateShotAsync(reloadShotTwo);
                        }
                    }
                    else
                    {
                        Debug.WriteLine("SaveShotAsync: no Shot2 id on frame; creating new Shot and linking it.");
                        var newShot = new Shot
                        {
                            ShotNumber = 2,
                            Ball = viewModel.SpareBallId,
                            Count = GetDownedPinsForShot(2),
                            LeaveType = viewModel.pinStates,
                            Frame = viewModel.CurrentFrame,
                            Comment = viewModel.Comment
                        };
                        shotId = await shotRepository.AddAsync(newShot);
                        if (reloadFrame != null)
                        {
                            reloadFrame.Result = viewModel.frameResult;
                            reloadFrame.Shot2 = shotId;
                            await frameRepository.UpdateFrameAsync(reloadFrame);
                            Debug.WriteLine($"Result4: {viewModel.frameResult}");
                        }
                    }
                }

                if (reloadFrame != null)
                    viewModel.currentFrameId = reloadFrame.FrameId;
            }

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
            bool shotExists = await frameRepository.DoesShotExistAsync(viewModel.gameId, viewModel.CurrentFrame, viewModel.CurrentShot);

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
                    Result = viewModel.frameResult,
                    GameId = Preferences.Get("GameID", 0),
                    Shot1 = viewModel.firstShotId
                };
                Debug.WriteLine($"Result1: {viewModel.frameResult}");
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
                        newFrame.Result = viewModel.frameResult;
                        Debug.WriteLine($"Result2: {viewModel.frameResult}");
                    }
                }
            }

            if (viewModel.CurrentShot.Equals(1) && !shotExists)
            {
                await frameRepository.AddFrame(newFrame);
                Debug.WriteLine("Frame added to db");
            }
            else
            {
                await frameRepository.UpdateFrameAsync(newFrame);
                viewModel.lastFrameId = newFrame.FrameId;
                Debug.WriteLine($"Frame updated {newFrame.Shot1} and {newFrame.Shot2}");
                Debug.WriteLine($"Frame result for updated frame: {newFrame.Result}");
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
                Debug.WriteLine($"Shot 1 count: {shot1.Count}");

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
            viewModel.TotalScore = totalScore;
            await UpdateGameAsync();
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
                            Debug.WriteLine($"Shot1 on reload count: {shot1?.Count}");
                        }

                        if (shotIds.Count > 1)
                        {
                            shot2 = await shotRepository.GetShotById(shotIds[1]);
                            Debug.WriteLine($"Shot2 on reload count: {shot2?.Count}");
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
                            viewModel.shot1PinStates = viewModel.pinStates;
                            ReloadButtonColors();
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

        private void HandleEditFrame()
        {
            var currentFrame = viewModel.Frames.FirstOrDefault(f => f.FrameNumber == viewModel.CurrentFrame);
            ReloadButtonColors(true);
            viewModel.OnPropertyChanged(nameof(viewModel.FrameDisplay));
            currentFrame.OnPropertyChanged(nameof(currentFrame.CenterPinColors));
            currentFrame.OnPropertyChanged(nameof(currentFrame.PinColors));
            viewModel.OnPropertyChanged(nameof(viewModel.Frames));
        }
    }
}
