using System;
using Microsoft.Maui.Storage;
using Cellular.ViewModel;
using System.Diagnostics;
using Cellular.Data;
using SQLitePCL;
using Microsoft.Maui.Controls;
using System.Collections.Generic;
using System.Linq;

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
        private void OnNextClicked(object sender, EventArgs e)
        {
            var currentFrame = viewModel.Frames.FirstOrDefault(f => f.FrameNumber == viewModel.currentFrame);
            if (currentFrame == null) return;

            if (viewModel.currentShot == 1)
            {
                ApplyPinColors(currentFrame);
                if (currentFrame.ShotOneBox == "X")
                {
                    viewModel.currentFrame++;
                    viewModel.currentShot = 1;
                }
                else
                {
                    viewModel.currentShot++;
                }
                viewModel.shot1PinStates = viewModel.pinStates;
            }
            else
            {
                ApplySecondShotColors(currentFrame);
                if (string.IsNullOrEmpty(currentFrame.ShotTwoBox))
                {
                    int downedPinsSecondShot = GetDownedPins() - int.Parse(currentFrame.ShotOneBox);
                    currentFrame.ShotTwoBox = downedPinsSecondShot.ToString();
                }
                viewModel.currentFrame++;
                viewModel.currentShot = 1;
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
            var currentFrame = viewModel.Frames.FirstOrDefault(f => f.FrameNumber == viewModel.currentFrame);
            if (currentFrame == null) return;

            int downedPins = GetDownedPins();

            if (viewModel.currentShot == 1)
            {
                currentFrame.ShotOneBox = downedPins == 10 ? "X" : downedPins.ToString();
            }
            else if (viewModel.currentShot == 2)
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
            var currentFrame = viewModel.Frames.FirstOrDefault(f => f.FrameNumber == viewModel.currentFrame);
            if (currentFrame == null) return;

            currentFrame.UpdateShotBox(viewModel.currentShot, "F");
            viewModel.OnPropertyChanged(nameof(viewModel.Frames));
        }

        private void OnGutterClicked(object sender, EventArgs e)
        {
            var currentFrame = viewModel.Frames.FirstOrDefault(f => f.FrameNumber == viewModel.currentFrame);
            if (currentFrame == null) return;

            var pins = new List<Button> { pin1, pin2, pin3, pin4, pin5, pin6, pin7, pin8, pin9, pin10 };

            if (viewModel.currentShot.Equals(1))
            {
                viewModel.pinStates = 0;
                foreach (var pin in pins)
                    pin.BackgroundColor = Color.FromArgb("#9880e5");
            }
            else
            {
                viewModel.pinStates = viewModel.shot1PinStates;

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

            currentFrame.UpdateShotBox(viewModel.currentShot, "_");
            viewModel.OnPropertyChanged(nameof(viewModel.Frames));
        }


        private void OnSpareClicked(object sender, EventArgs e)
        {
            var currentFrame = viewModel.Frames.FirstOrDefault(f => f.FrameNumber == viewModel.currentFrame);
            if (currentFrame == null || viewModel.currentShot == 1) return;

            currentFrame.UpdateShotBox(viewModel.currentShot, "/");
            viewModel.pinStates = 0; // All pins knocked down

            var pins = new List<Button> { pin1, pin2, pin3, pin4, pin5, pin6, pin7, pin8, pin9, pin10 };
            foreach (var pin in pins) pin.BackgroundColor = Colors.LightSlateGrey;

            viewModel.OnPropertyChanged(nameof(viewModel.pinStates));
        }

        private void OnStrikeClicked(object sender, EventArgs e)
        {
            var currentFrame = viewModel.Frames.FirstOrDefault(f => f.FrameNumber == viewModel.currentFrame);
            if (currentFrame == null || viewModel.currentShot == 2) return;

            viewModel.pinStates = 0; // All pins knocked down
            currentFrame.UpdateShotBox(viewModel.currentShot, "X");

            var pins = new List<Button> { pin1, pin2, pin3, pin4, pin5, pin6, pin7, pin8, pin9, pin10 };
            foreach (var pin in pins) pin.BackgroundColor = Colors.LightSlateGrey;

            viewModel.OnPropertyChanged(nameof(viewModel.Frames));
        }
    }
}
