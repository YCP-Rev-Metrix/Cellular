using Cellular.Data;
using Cellular.ViewModel;
using CommunityToolkit.Maui.Views;
using Microsoft.Maui.Controls;
using System.Threading.Tasks;
using System.Diagnostics;

namespace Cellular.Views
{
    public partial class EstablishmentEditorPopup : Popup
    {
        public TaskCompletionSource<Establishment?> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Establishment Establishment { get; set; }

        public EstablishmentEditorPopup(Establishment esta)
        {
            InitializeComponent();
            Establishment = esta;
            BindingContext = Establishment;
        }

        private async void OnSaveClicked(object sender, System.EventArgs e)
        {
            // BindingContext is the Establishment object and has been edited via two-way binding
            Completion.TrySetResult(Establishment);
            await CloseAsync();
        }

        private async void OnCancelClicked(object sender, System.EventArgs e)
        {
            Completion.TrySetResult(null);
            await CloseAsync();
        }
    }
}
