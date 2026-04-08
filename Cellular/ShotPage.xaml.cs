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
                // Ensure frame backgrounds are computed on initial load
                viewModel.RefreshFrameBackgrounds();
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

        // Handles pin click events and toggles the pin state
        private void OnPinClicked(object sender, EventArgs e)
        {
            if (viewModel.GameCompleted == true && !viewModel.EditMode) return;
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
                else // Shot 2
                {
                    bool wasUpInShot1 = (viewModel.shot1PinStates & pinBit) != 0;
                    bool wasFoul = (viewModel.shot1PinStates & (1 << 10)) != 0;

                    // If it was already down or a foul occurred, we can't interact with it
                    if (!wasUpInShot1 && !wasFoul)
                    {
                        return;
                    }

                    // Toggle the bit in the current state
                    viewModel.pinStates ^= (short)pinBit;

                    // Check the NEW state after toggling
                    bool isCurrentlyUp = (viewModel.pinStates & pinBit) != 0;

                    if (isCurrentlyUp)
                    {
                        // Pin is standing (1) -> Should be Red
                        button.BackgroundColor = Colors.Red;
                    }
                    else
                    {
                        // Pin was knocked down (0) -> Should be Purple
                        button.BackgroundColor = Color.FromArgb("#9880e5");
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

            if (currentFrame == null || (viewModel.GameCompleted == true && !viewModel.EditMode) || viewModel.CurrentFrame > 12) return;

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
                //Caclulate the shot type and save
                viewModel.pinStates = ShotCalculator.CalculateShotType(viewModel.pinStates, 1);
                //Save shot to DB
                viewModel.firstShotId = await SaveShotAsync(1);

                ApplyFirstShotColors(currentFrame);

                SetIsGameOver(); //Check if the game is over

                if (currentFrame.ShotOneBox == "X")
                {
                    //Save the frame to the database
                    await SaveFrameAsync(true, false);

                    if (viewModel.GameCompleted == false)
                    {
                        viewModel.CurrentFrame++;

                        if (!viewModel.EditMode)
                        {
                            viewModel.CurrentShot = 1;
                        }
                        else
                        {
                            await LoadExistingGameData(true);
                        }
                    }
                    if (viewModel.CurrentFrame <= 12)
                    {
                        // Only add a new UI frame if one for this frame number doesn't already exist
                        if (!viewModel.Frames.Any(f => f.FrameNumber == viewModel.CurrentFrame))
                        {
                            ShotPageFrame frame = new ShotPageFrame(viewModel.CurrentFrame);
                            viewModel.Frames.Add(frame);
                        }
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
                //Caclulate the shot type and save
                viewModel.pinStates = ShotCalculator.CalculateShotType(viewModel.pinStates, 2);
                //Save shot to DB
                viewModel.secondShotId = await SaveShotAsync(2);

                ApplySecondShotColors(currentFrame);
                if (string.IsNullOrEmpty(currentFrame.ShotTwoBox))
                {
                    int firstShotValue = ParseShotBoxValue(currentFrame.ShotOneBox);
                    int downedPinsSecondShot = GetDownedPinsForShot(2) - firstShotValue;
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
                    viewModel.frameResult = "Open";
                    await SaveFrameAsync(false, false);
                    Debug.WriteLine("Frame is saved as neither");
                }
                if (currentFrame.ShotTwoBox == "/" && viewModel.CurrentFrame == 10)
                {
                    // Only add frame 11 if it doesn't already exist in the UI collection
                    if (!viewModel.Frames.Any(f => f.FrameNumber == 11))
                    {
                        ShotPageFrame frame = new ShotPageFrame(11);
                        viewModel.Frames.Add(frame);
                    }
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
                }

                //Add next frame view frame (avoid duplicates)
                if(viewModel.CurrentFrame <= 10)
                {
                    if (!viewModel.Frames.Any(f => f.FrameNumber == viewModel.CurrentFrame))
                    {
                        ShotPageFrame frame = new ShotPageFrame(viewModel.CurrentFrame);
                        viewModel.Frames.Add(frame);
                    }
                }

                if (viewModel.EditMode)
                {
                    viewModel.EditMode = false;
                    await LoadExistingGameData(true);
                }
            }
            viewModel.Comment = "";
            await UpdateScore();
            viewModel.RefreshFrameBackgrounds();
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
                    currentFrame.ShotTwoBox = " "; //Clear second shot in case of edit mode
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

        private void ApplyFirstShotColors(ShotPageFrame currentFrame)
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
        private void ReloadButtonColors(int shotNum = 1)
        {
            var pins = new List<Button> { pin1, pin2, pin3, pin4, pin5, pin6, pin7, pin8, pin9, pin10 };

            for (int i = 0; i < 10; i++)
            {
                int pinBit = 1 << i;

                // Only READ the states, do not use &= or |= here
                bool wasUpInShot1 = (viewModel.shot1PinStates & pinBit) != 0;
                bool isCurrentlyUp = (viewModel.pinStates & pinBit) != 0;

                if (shotNum == 2)
                {
                    if (wasUpInShot1 && isCurrentlyUp)
                    {
                        pins[i].BackgroundColor = Colors.Red; // Standing in both
                    }
                    else if (wasUpInShot1 && !isCurrentlyUp)
                    {
                        pins[i].BackgroundColor = Color.FromArgb("#9880e5"); // Was up, now down
                    }
                    else
                    {
                        pins[i].BackgroundColor = Colors.LightSlateGray; // Was already down
                    }
                }
                else // Shot 1
                {
                    pins[i].BackgroundColor = isCurrentlyUp ? Color.FromArgb("#9880e5") : Colors.LightSlateGray;
                }
            }
        }

        private void OnFoulClicked(object sender, EventArgs e)
        {
            var currentFrame = viewModel.Frames.FirstOrDefault(f => f.FrameNumber == viewModel.CurrentFrame);
            if (currentFrame == null || (viewModel.GameCompleted == true && !viewModel.EditMode)) return;

            // Toggle the 11th bit (foul)
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
            if (currentFrame == null || (viewModel.GameCompleted == true && !viewModel.EditMode)) return;

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
            if (currentFrame == null || viewModel.CurrentShot == 1 || (viewModel.GameCompleted == true && !viewModel.EditMode)) return;

            currentFrame.UpdateShotBox(viewModel.CurrentShot, "/");
            viewModel.pinStates &= unchecked((short)~0x03FF); // All pins knocked down

            var pins = new List<Button> { pin1, pin2, pin3, pin4, pin5, pin6, pin7, pin8, pin9, pin10 };
            foreach (var pin in pins) pin.BackgroundColor = Colors.LightSlateGrey;

            viewModel.OnPropertyChanged(nameof(viewModel.pinStates));
        }

        private void OnStrikeClicked(object sender, EventArgs e)
        {
            var currentFrame = viewModel.Frames.FirstOrDefault(f => f.FrameNumber == viewModel.CurrentFrame);
            if (currentFrame == null || viewModel.CurrentShot == 2 || (viewModel.GameCompleted == true && !viewModel.EditMode)) return;

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

                            if (viewModel.EditMode && reloadShotOne.Count == 10)
                            {
                                Debug.WriteLine($"Viewmodel shot 2 id is {reloadFrame.Shot2.Value}");
                                var shot2 = await shotRepository.GetShotById(reloadFrame.Shot2.Value);
                                if (shot2 != null)
                                {
                                    Debug.WriteLine("Trying to delete old shot 2");
                                    await shotRepository.DeleteShotAsync(shot2);
                                    // Make sure the frame no longer references the deleted shot
                                    try
                                    {
                                        reloadFrame.Shot2 = null;
                                        reloadFrame.Result = viewModel.frameResult;
                                        await frameRepository.UpdateFrameAsync(reloadFrame);
                                        Debug.WriteLine($"Cleared Shot2 reference from frame {reloadFrame.FrameId}");
                                    }
                                    catch (Exception ex)
                                    {
                                        Debug.WriteLine($"Error clearing Shot2 on frame update: {ex.Message}");
                                    }
                                }
                            }
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

            BowlingFrame frame;
            string result = null;
            bool shotExists = await frameRepository.DoesShotExistAsync(viewModel.gameId, viewModel.CurrentFrame, viewModel.CurrentShot);

            if (strike)
            {
                result = "strike";
            } else if (spare) {
                result = "spare";
            }

            //If shot 1
            if (viewModel.CurrentShot.Equals(1))
            {
                Debug.WriteLine(Preferences.Get("GameID", 0));
                frame = new BowlingFrame
                {
                    FrameNumber = viewModel.CurrentFrame,
                    Lane = null,
                    Result = viewModel.frameResult,
                    GameId = Preferences.Get("GameID", 0),
                    Shot1 = viewModel.firstShotId,
                    Shot2 = viewModel.secondShotId
                };
                Debug.WriteLine($"Result1: {viewModel.frameResult}");
            }
            else //Shot 2
            {
                frame = await frameRepository.GetFrameById(viewModel.currentFrameId);
                if (!strike && viewModel.CurrentShot == 2)
                {
                    if (frame != null)
                    {
                        Debug.WriteLine("NOT NULL");
                        frame.Shot2 = viewModel.secondShotId;
                        Debug.WriteLine($"Shot 2 id is {viewModel.secondShotId}");
                        frame.Result = viewModel.frameResult;
                        Debug.WriteLine($"Result2: {viewModel.frameResult}");
                    }
                }
            }

            //Save new frame or update old frame
            if (viewModel.CurrentShot.Equals(1) && !shotExists)
            {
                await frameRepository.AddFrame(frame);
                Debug.WriteLine("Frame added to db");
            }
            else
            {
                await frameRepository.UpdateFrameAsync(frame);
                viewModel.lastFrameId = frame.FrameId;
                Debug.WriteLine($"Frame updated {frame.Shot1} and {frame.Shot2}");
                Debug.WriteLine($"Frame result for updated frame: {frame.Result}");
            }

            // Retrieve the inserted frame ID
            frame.FrameId = (await database.GetConnection().Table<BowlingFrame>()
                                         .OrderByDescending(f => f.FrameId)
                                         .FirstOrDefaultAsync())?.FrameId ?? 0;

            viewModel.currentFrameId = frame.FrameId;
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

        private async Task LoadExistingGameData(bool fullRefresh = false)
        {
            var db = new CellularDatabase().GetConnection();
            var gameRepository = new GameRepository(db);
            var frameRepository = new FrameRepository(db);
            var shotRepository = new ShotRepository(db);

            await gameRepository.InitAsync();
            await frameRepository.InitAsync();
            await shotRepository.InitAsync();

            //If full refresh, refresh page (Used for edit mode)
            if (fullRefresh)
            {
                viewModel.Frames.Clear();
                viewModel.CurrentFrame = 1;
                viewModel.CurrentShot = 1;
                viewModel.pinStates = 0;
                viewModel.shot1PinStates = 0;
            }

            // Retrieve the current game
            var game = await gameRepository.GetGame(viewModel.currentSession, viewModel.currentGame, viewModel.UserId);
            List<int> frameIds = await frameRepository.GetFrameIdsByGameIdAsync(Preferences.Get("GameID", 0));

            Debug.WriteLine($"Frame Ids to load: {string.Join(", ", frameIds)}");

            // Ensure there is at least one UI frame, and only create UI frames up to the number
            // of saved frames (so we don't add unnecessary blank frames when reloading a game
            // that hasn't completed).
            if (viewModel.Frames.Count == 0)
            {
                viewModel.Frames.Add(new ShotPageFrame(1));
            }

            // Add UI frames so there is one UI frame per saved frame.
            // Use '<' (not '<=') to avoid creating an extra frame (off-by-one).
            while (viewModel.Frames.Count < frameIds.Count)
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
                    // Ensure viewModel.CurrentFrame matches the frame being loaded so helper logic
                    // and SetIsGameOver behave the same as when the game was played
                    viewModel.CurrentFrame = existingFrame.FrameNumber;
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

                        // Process shot 1 and shot 2 using helpers to avoid duplication
                        if (shot1 != null)
                        {
                            ProcessLoadedShotOne(existingFrame, shot1);
                        }

                        if (shot2 != null)
                        {
                            ProcessLoadedShotTwo(existingFrame, shot2);
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

            // After loading frames, mirror OnNextClicked behavior: if the last saved frame was a strike
            // or the 10th frame was a spare, advance the UI by adding the next frame.
            if (frameIds.Any())
            {
                var lastFrameObj = await frameRepository.GetFrameById(frameIds.Last());
                if (lastFrameObj != null)
                {
                    int lastFrameNumber = lastFrameObj.FrameNumber ?? viewModel.Frames.Count;
                    int nextFrameNumber = lastFrameNumber + 1;

                    bool lastWasStrike = false;
                    bool lastWasSpare10 = false;

                    if (lastFrameObj.Shot1.HasValue)
                    {
                        var lastShot1 = await shotRepository.GetShotById(lastFrameObj.Shot1.Value);
                        if (lastShot1 != null && lastShot1.Count == 10)
                            lastWasStrike = true;
                    }

                    if (lastFrameNumber == 10 && lastFrameObj.Shot1.HasValue && lastFrameObj.Shot2.HasValue)
                    {
                        var s1 = await shotRepository.GetShotById(lastFrameObj.Shot1.Value);
                        var s2 = await shotRepository.GetShotById(lastFrameObj.Shot2.Value);
                        if (s1 != null && s2 != null && (s1.Count + s2.Count == 10))
                            lastWasSpare10 = true;
                    }

                    if ((lastWasStrike || lastWasSpare10) && nextFrameNumber <= 12)
                    {
                        if (!viewModel.Frames.Any(f => f.FrameNumber == nextFrameNumber))
                        {
                            viewModel.Frames.Add(new ShotPageFrame(nextFrameNumber));
                        }

                        viewModel.CurrentFrame = nextFrameNumber;
                        viewModel.CurrentShot = 1;
                    }
                }
            }

            await UpdateScore();
            // Notify the viewModel of frame updates
            viewModel.OnPropertyChanged(nameof(viewModel.Frames));
            // Recalculate UI frame backgrounds now that frames and current frame are loaded
            viewModel.RefreshFrameBackgrounds();
        }

        private void HandleEditFrame()
        {
            var currentFrame = viewModel.Frames.FirstOrDefault(f => f.FrameNumber == viewModel.CurrentFrame);
            Debug.WriteLine($"Current shot for edit mode is {viewModel.CurrentShot}");
            UpdateShotBoxes();
            ReloadButtonColors(viewModel.CurrentShot);
            viewModel.OnPropertyChanged(nameof(viewModel.FrameDisplay));
            currentFrame.OnPropertyChanged(nameof(currentFrame.CenterPinColors));
            currentFrame.OnPropertyChanged(nameof(currentFrame.PinColors));
            viewModel.OnPropertyChanged(nameof(viewModel.Frames));
        }

        // Helper used when reloading a saved game: process the first shot UI/state update
        private void ProcessLoadedShotOne(ShotPageFrame existingFrame, Shot shot1)
        {
            if (shot1 == null) return;

            // Restore the leave type stored for shot1 into pinStates
            viewModel.pinStates = (short)shot1.LeaveType;
            UpdateShotBoxes();
            ApplyFirstShotColors(existingFrame);
            SetIsGameOver(); //Check if the game is over

            if (existingFrame.ShotOneBox.Equals("X"))
            {
                if (viewModel.CurrentFrame == 10)
                {
                    // Only add frame 11 if one doesn't already exist
                    if (!viewModel.Frames.Any(f => f.FrameNumber == 11))
                    {
                        ShotPageFrame newFrame = new ShotPageFrame(11);
                        viewModel.Frames.Add(newFrame);
                    }
                }
                if (viewModel.CurrentFrame == 11)
                {
                    // Only add frame 12 if one doesn't already exist
                    if (!viewModel.Frames.Any(f => f.FrameNumber == 12))
                    {
                        ShotPageFrame newFrame = new ShotPageFrame(12);
                        viewModel.Frames.Add(newFrame);
                    }
                }
                if (viewModel.GameCompleted != true && viewModel.CurrentFrame < viewModel.Frames.Count)
                {
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

        // Helper used when reloading a saved game: process the second shot UI/state update
        private void ProcessLoadedShotTwo(ShotPageFrame existingFrame, Shot shot2)
        {
            if (shot2 == null) return;

            viewModel.pinStates = (short)shot2.LeaveType;
            ApplySecondShotColors(existingFrame);
            UpdateShotBoxes();
            SetIsGameOver(); //Check if the game is over

            if (string.IsNullOrEmpty(existingFrame.ShotTwoBox))
            {
                int firstShotValue = ParseShotBoxValue(existingFrame.ShotOneBox);
                int downedPinsSecondShot = GetDownedPinsForShot(2) - firstShotValue;
                existingFrame.ShotTwoBox = downedPinsSecondShot.ToString();
            }

            int downedPins = GetDownedPinsForFrameView(viewModel.shot1PinStates, viewModel.pinStates);

            if (existingFrame.ShotTwoBox == "/" && viewModel.CurrentFrame == 10)
            {
                // Only add frame 11 if it isn't already present
                if (!viewModel.Frames.Any(f => f.FrameNumber == 11))
                {
                    ShotPageFrame newFrame = new ShotPageFrame(11);
                    viewModel.Frames.Add(newFrame);
                }
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

        // Safely parse a shot box string ("X","-","/","F" or numeric) into an integer pin count
        private int ParseShotBoxValue(string box)
        {
            if (string.IsNullOrEmpty(box)) return 0;
            box = box.Trim();
            if (box == "X") return 10;
            if (box == "-") return 0;
            if (box == "F") return 0;
            if (box == "/") return 10; // caller should handle spare context appropriately
            if (int.TryParse(box, out int val)) return val;
            return 0;
        }
    }
}
