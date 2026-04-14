using Cellular.ViewModel;
using CommunityToolkit.Maui.Views;

namespace Cellular.Views
{
    public partial class ShotPopup : Popup
    {
        public ShotPopup(GameInterfaceViewModel viewModel)
        {
            InitializeComponent();
            BindingContext = viewModel;
        }

        private async void OnCloseClicked(object sender, System.EventArgs e)
        {
            await CloseAsync();
        }
    }
}
