using System;
using Microsoft.Maui.Storage;
using Cellular.ViewModel;
using System.Diagnostics;

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

        public void BoardChanged(object sender, EventArgs e)
        {
            if (sender is Slider slider)
            {
                double roundedValue = Math.Round(slider.Value);

                if (TestingLabel != null) // Ensure TestingLabel is not null
                {
                    // Show "Gutter" for values 0 and 40, otherwise display the value
                    TestingLabel.Text = (roundedValue == 0 || roundedValue == 40) ? "Gutter" : roundedValue.ToString();
                }
            }
            else
            {
                Console.WriteLine("BoardChanged: Sender is not a Slider.");
            }
        }


    }

}
