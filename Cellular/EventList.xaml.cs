using Cellular.ViewModel;
using Cellular.Views;
using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Extensions;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Storage;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;

namespace Cellular
{
    public partial class EventList : ContentPage
    {
        private readonly SessionListViewModel viewModel;
        private Dictionary<string, StackLayout> sessionGames;
        public EventList()
        {
            InitializeComponent();
            viewModel = new SessionListViewModel();
            BindingContext = viewModel;
            sessionGames = new Dictionary<string, StackLayout>();
            LoadDataAsync();
        }

        private async void LoadDataAsync()
        {
            await viewModel.loadEvents();
        }

        private async void AddEvent(object sender, EventArgs e)
        {
            await this.ShowPopupAsync(new EventPopup(), new PopupOptions
            {
                Shape = new RoundRectangle
                {
                    CornerRadius = new CornerRadius(20),
                    StrokeThickness = 0
                }
            });
            LoadDataAsync();
        }

        // Handles clicks from the dynamically-created event buttons.
        private async void OnEventButtonClicked(object sender, EventArgs e)
        {
            if (sender is Button btn && btn.CommandParameter is Event ev)
            {
                Preferences.Set("EventId", ev.EventId);
                // pass event id and name to SessionList (constructor)
                Debug.WriteLine($"Event button clicked: {ev.EventId} - {ev.Name}");
                await Navigation.PushAsync(new SessionList(ev.EventId, ev.Name ?? string.Empty));
            }
        }
    }
}
