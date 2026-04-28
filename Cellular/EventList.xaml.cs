using Cellular.Data;
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
        private readonly EventRepository _eventRepo;
        private Dictionary<string, StackLayout> sessionGames;

        public EventList()
        {
            InitializeComponent();
            viewModel = new SessionListViewModel();
            BindingContext = viewModel;
            sessionGames = new Dictionary<string, StackLayout>();
            _eventRepo = new EventRepository(new CellularDatabase().GetConnection());
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

        // Handles taps on the event info card — navigates to that event's sessions.
        private async void OnEventCardTapped(object sender, TappedEventArgs e)
        {
            if (e.Parameter is Event ev)
            {
                Preferences.Set("EventId", ev.EventId);
                Debug.WriteLine($"Event card tapped: {ev.EventId} - {ev.LongName}");
                await Navigation.PushAsync(new SessionList(ev.EventId, ev.NickName ?? string.Empty));
            }
        }

        // Handles clicks on the edit (✏) button — opens the editor popup for that event.
        private async void OnEditEventClicked(object sender, EventArgs e)
        {
            if (sender is not Button btn || btn.CommandParameter is not Event ev)
                return;

            try
            {
                var popup = new EventEditorPopup(ev);

                // Guard against the popup being dismissed without a result (e.g. OS back gesture)
                popup.Closed += (s, args) =>
                {
                    if (!popup.Completion.Task.IsCompleted)
                        popup.Completion.TrySetResult(null);
                };

                this.ShowPopup(popup);

                var result = await popup.Completion.Task;
                if (result != null)
                {
                    await _eventRepo.UpdateAsync(result);
                    LoadDataAsync();
                    await DisplayAlertAsync("Event", "Event saved.", "OK");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error editing event: {ex.Message}");
                await DisplayAlertAsync("Error", $"Could not save event: {ex.Message}", "OK");
            }
        }
    }
}
