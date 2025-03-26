using System;
using Microsoft.Maui.Storage;
using Cellular.ViewModel;
using System.Diagnostics;
using Cellular.Data;
using SQLitePCL;
using Microsoft.Maui.Controls;

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
            _ = viewModel.LoadUserHand();
            Debug.WriteLine("Hand: " + viewModel.Hand);

        }

        public void BoardChanged(object sender, EventArgs e)
        {
            if (sender is Slider slider)
            {
                double roundedValue = Math.Round(slider.Value);

                if (TestingLabel != null) // Ensure TestingLabel is not null
                {
                    if(viewModel.Hand == "Left")
                    {
                        // Show "Gutter" for values 0 and 40, otherwise display the value
                        TestingLabel.Text = (roundedValue == 0 || roundedValue == 40) ? "Gutter" : roundedValue.ToString();
                    }
                    else
                    {
                        int roundedValue2 = (int)Math.Round(slider.Value); // Ensure proper rounding
                        int invertedValue = 40 - roundedValue2;
                        TestingLabel.Text = (invertedValue == 0 || invertedValue == 40) ? "Gutter" : invertedValue.ToString();

                    }
                }
            }
            else
            {
                Console.WriteLine("BoardChanged: Sender is not a Slider.");
            }
        }

        //Responsible for tracking which pins are up or down and changing their colors on the screen
        private void OnPinClicked(object sender, EventArgs e)
        {
            if (sender is Button button)
            {
                // Toggle button color
                if (button.BackgroundColor == Colors.LightSlateGrey)
                {
                    button.BackgroundColor = Color.FromArgb("#9880e5"); // Selected color
                }
                else
                {
                    button.BackgroundColor = Colors.LightSlateGrey; // Default color
                }

                // Get the pin number from the button text
                if (int.TryParse(button.Text, out int pinNumber) && pinNumber >= 1 && pinNumber <= 10)
                {
                    // Toggle the bit in pinStates
                    viewModel.pinStates ^= (short)(1 << (pinNumber - 1));

                    // Get the new state of the pin (0 or 1)
                    int pinState = (viewModel.pinStates & (1 << (pinNumber - 1))) != 0 ? 1 : 0;

                    // Print the pin number and its new state to the console
                    Console.WriteLine($"Pin {pinNumber} is now {(pinState == 1 ? "Up" : "Down")}");

                    // Notify the ViewModel that pinStates has changed (if needed)
                    viewModel.OnPropertyChanged(nameof(viewModel.pinStates));
                }
            }
        }

        private void OnNextClicked(object sender, EventArgs e)
        {
            var currentFrame = viewModel.Frames.FirstOrDefault(f => f.FrameNumber == viewModel.currentFrame);
            int currentShot = viewModel.currentShot;
            short pinStates = viewModel.pinStates; // Current frame's pin states

            if (currentFrame == null)
            {
                Console.WriteLine($"No frame found for {viewModel.currentFrame}");
                return;
            }

            if (currentShot == 1)
            {
                // Update frame view pins
                for (int i = 0; i < 10; i++)
                {
                    bool isPinDown = (pinStates & (1 << i)) != 0;
                    currentFrame.UpdateCenterPinColor(i, isPinDown ? Colors.Transparent : Colors.White);
                }

                if (currentFrame.ShotOneBox == "X") // Check if strike
                {
                    viewModel.currentFrame++;
                }
                if (string.IsNullOrEmpty(currentFrame.ShotOneBox)) // If empty, update with downed pins count
                {
                    currentFrame.ShotOneBox = GetDownedPins().ToString();
                }

                viewModel.shot1PinStates = pinStates; // Save first shot's state
                viewModel.currentShot++;
            }
            else // Second shot logic
            {
                short firstShotPinStates = viewModel.shot1PinStates; // Pins state from first shot

                for (int i = 0; i < 10; i++)
                {
                    bool wasStanding = (firstShotPinStates & (1 << i)) != 0; // Pin was standing at the end of shot 1
                    bool isNowDown = (pinStates & (1 << i)) == 0; // Pin is now down in shot 2

                    if (wasStanding && isNowDown)
                    {
                        // Pin was standing but now down → Light Gray
                        currentFrame.UpdatePinColor(i, Colors.LightGray);
                    }
                    else if (wasStanding)
                    {
                        // Pin is still standing → Red
                        currentFrame.UpdatePinColor(i, Colors.Red);
                    }
                }

                if (string.IsNullOrEmpty(currentFrame.ShotTwoBox))
                {
                    if (currentFrame.ShotOneBox.Equals("_"))
                    {
                        currentFrame.ShotTwoBox = GetDownedPins().ToString();
                    }
                    else
                    {
                        int downedPinsSecondShot = GetDownedPins() - int.Parse(currentFrame.ShotOneBox);
                        currentFrame.ShotTwoBox = downedPinsSecondShot.ToString();
                    }
                }

                viewModel.currentFrame++;
                viewModel.currentShot = 1;
            }

            // Notify the UI that pin colors have changed
            currentFrame.OnPropertyChanged(nameof(currentFrame.CenterPinColors));
            currentFrame.OnPropertyChanged(nameof(currentFrame.PinColors));
        }


        private int GetDownedPins()
        {
            int pinsDowned = 0;
            int shot1PinsDowned = 0;
            short pinStates = viewModel.pinStates;
            short shot1PinStates = viewModel.shot1PinStates;

            for (int i = 0; i < 10; i++)
            {
                if ((pinStates & (1 << i)) == 0) // If bit is 0, pin is down
                {
                    pinsDowned++;
                }
            }

            if (viewModel.currentShot == 1)
            {
                return pinsDowned;
            }
            else
            {
                return pinsDowned - shot1PinsDowned;
            }

        }

        private void OnFoulClicked(object sender, EventArgs e)
        {
            var currentFrame = viewModel.Frames.FirstOrDefault(f => f.FrameNumber == viewModel.currentFrame);

            if (currentFrame == null)
            {
                return;
            }

            currentFrame.UpdateShotBox(viewModel.currentShot, "F");
            // Notify UI about changes
            viewModel.OnPropertyChanged(nameof(viewModel.Frames));
        }


        private void OnBlankClicked(object sender, EventArgs e)
        {
            var currentFrame = viewModel.Frames.FirstOrDefault(f => f.FrameNumber == viewModel.currentFrame);

            if (currentFrame == null)
            {
                return;
            }

            currentFrame.UpdateShotBox(viewModel.currentShot, "_");
            // Notify UI about changes
            viewModel.OnPropertyChanged(nameof(viewModel.Frames));
        }

        private void OnSpareClicked(object sender, EventArgs e)
        {
            var currentFrame = viewModel.Frames.FirstOrDefault(f => f.FrameNumber == viewModel.currentFrame);

            if (currentFrame == null || viewModel.currentShot == 1)
            {
                return;
            }

            currentFrame.UpdateShotBox(viewModel.currentShot, "/");
            // Notify UI about changes
            viewModel.OnPropertyChanged(nameof(viewModel.Frames));
        }

        private void OnStrikeClicked(object sender, EventArgs e)
        {
            var currentFrame = viewModel.Frames.FirstOrDefault(f => f.FrameNumber == viewModel.currentFrame);

            if (currentFrame == null || viewModel.currentShot == 2)
            {
                return;
            }

            currentFrame.UpdateShotBox(viewModel.currentShot, "X");
            // Notify UI about changes
            viewModel.OnPropertyChanged(nameof(viewModel.Frames));
        }

    }

}
