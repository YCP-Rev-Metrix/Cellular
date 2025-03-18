using System;
using Microsoft.Maui.Storage;
using Cellular.ViewModel;

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

        public void boardChanged(object sender, EventArgs e)
        {
            if (TestingLabel != null) // Ensure TestingLabel is not null
            {
                Slider typedSender = (Slider)sender;
                // Ensure you only set the value if it is not null
                if (typedSender != null)
                {
                    double roundedValue = Math.Round(typedSender.Value);
                    TestingLabel.Text = roundedValue.ToString(); // Update the label text
                }
            }
            else
            {
                Console.WriteLine("TestingLabel is null.");  // Log for debugging purposes
            }
        }

    }

}
