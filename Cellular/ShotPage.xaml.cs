using System;
using Microsoft.Maui.Storage;
using Cellular.ViewModel;
using System.Diagnostics;
using Cellular.Data;
using SQLitePCL;

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
                        double invertedValue = 40 - roundedValue;
                        TestingLabel.Text = (invertedValue == 0 || invertedValue == 40) ? "Gutter" : invertedValue.ToString();

                    }
                }
            }
            else
            {
                Console.WriteLine("BoardChanged: Sender is not a Slider.");
            }
        }


    }

}
