using CommunityToolkit.Maui.Core;
using CommunityToolkit.Maui.Views;
using System.Threading.Tasks;
using System.ComponentModel;
using Cellular.ViewModel;
using System.Runtime.CompilerServices;

namespace Cellular.Views
{
    public partial class StatsPopup : Popup, INotifyPropertyChanged
    {
        internal StatsPopup(StatsViewModel vm)
        {
            InitializeComponent();
            this.BindingContext = vm;
        }

        private async void OnCloseClicked(object sender, EventArgs e)
        {
            await CloseAsync();
        }
    }
}