using CommunityToolkit.Maui.Core;
using CommunityToolkit.Maui.Views;
using System.Threading.Tasks;

namespace Cellular.Views
{
    public partial class MmsConnectionPopup : Popup
    {
        public TaskCompletionSource<bool?> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public MmsConnectionPopup(string macAddress, bool isConnected)
        {
            InitializeComponent();

            if (isConnected && !string.IsNullOrEmpty(macAddress))
            {
                MacAddressLabel.Text = $"MAC Address: {macAddress}";
                StatusLabel.Text = "Status: Connected";
                StatusLabel.TextColor = Colors.Green;
                ConnectButton.IsVisible = false;
                DisconnectButton.IsVisible = true;
            }
            else
            {
                // Show saved MAC address if available, otherwise show "Not Connected"
                if (!string.IsNullOrEmpty(macAddress) && macAddress != "Unknown")
                {
                    MacAddressLabel.Text = $"MAC Address: {macAddress}";
                }
                else
                {
                    MacAddressLabel.Text = "MAC Address: Not Connected";
                }
                StatusLabel.Text = "Status: Disconnected";
                StatusLabel.TextColor = Colors.Red;
                ConnectButton.IsVisible = true;
                DisconnectButton.IsVisible = false;
            }
        }

        private async void OnConnectClicked(object sender, EventArgs e)
        {
            // Return false to indicate connect was requested
            Completion.TrySetResult(false);
            await CloseAsync();
        }

        private async void OnDisconnectClicked(object sender, EventArgs e)
        {
            // Return true to indicate disconnect was requested
            Completion.TrySetResult(true);
            await CloseAsync();
        }

        private async void OnCloseClicked(object sender, EventArgs e)
        {
            // Return null to indicate just closing
            Completion.TrySetResult(null);
            await CloseAsync();
        }
    }
}
