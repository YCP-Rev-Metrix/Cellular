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

            if (currentFrame == null)
            {
                Console.WriteLine($"No frame found for {viewModel.currentFrame}");
                return;
            }

            short pinStates = viewModel.pinStates; // Use the pin states for the current frame

            // Update the pin colors based on the pin states
            for (int i = 0; i < 10; i++)
            {
                bool isPinDown = (pinStates & (1 << i)) != 0;
                currentFrame.CenterPinColors[i] = isPinDown ? Colors.White : Colors.Transparent; 
            }

            // Refresh the CollectionView to show updated data
            FramesCollectionView.ItemsSource = null;
            FramesCollectionView.ItemsSource = viewModel.Frames;

            // Notify the frame has been updated
            viewModel.OnPropertyChanged(nameof(viewModel.Frames));
        }

    }

}
