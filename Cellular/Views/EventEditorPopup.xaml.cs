using Cellular.Data;
using Cellular.ViewModel;
using CommunityToolkit.Maui.Views;
using Microsoft.Maui.Storage;

namespace Cellular.Views
{
    public partial class EventEditorPopup : Popup
    {
        public TaskCompletionSource<Event?> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Event Event { get; }

        private readonly EstablishmentRepository _estRepo;

        public EventEditorPopup(Event ev)
        {
            InitializeComponent();
            Event = ev;
            BindingContext = Event;

            _estRepo = new EstablishmentRepository(new CellularDatabase().GetConnection());

            // Initialise the TimePicker from the stored string (e.g. "6:30 PM")
            if (!string.IsNullOrWhiteSpace(ev.StartTime) &&
                DateTime.TryParse(ev.StartTime, out var parsed))
            {
                StartTimePicker.Time = parsed.TimeOfDay;
            }
            else
            {
                StartTimePicker.Time = new TimeSpan(18, 0, 0); // default 6:00 PM
            }

            // Initialise the Stepper and its label from the stored value
            int games = ev.NumGamesPerSession > 0 ? ev.NumGamesPerSession : 3;
            GamesStepper.Value = games;
            GamesLabel.Text = games.ToString();

            // Load establishments and pre-select the current one
            _ = LoadEstablishmentsAsync(ev.Location);
        }

        private async Task LoadEstablishmentsAsync(string? currentLocation)
        {
            int userId = Preferences.Get("UserId", 0);
            var estabs = await _estRepo.GetEstablishmentsByUserIdAsync(userId);

            EstablishmentPicker.ItemsSource = estabs;

            // Pre-select whichever establishment matches the stored Location (NickName)
            if (!string.IsNullOrWhiteSpace(currentLocation))
            {
                var match = estabs.FirstOrDefault(e =>
                    string.Equals(e.NickName, currentLocation, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                    EstablishmentPicker.SelectedItem = match;
            }
        }

        // Keep the label in sync as the stepper moves
        private void OnGamesStepperValueChanged(object sender, ValueChangedEventArgs e)
        {
            GamesLabel.Text = ((int)e.NewValue).ToString();
        }

        private async void OnSaveClicked(object sender, System.EventArgs e)
        {
            // Write back the controls that can't use two-way binding directly
            Event.StartTime = DateTime.Today.Add(StartTimePicker.Time ?? new TimeSpan(18, 0, 0))
                .ToString("h:mm tt");
            Event.NumGamesPerSession = (int)GamesStepper.Value;

            // Store the selected establishment's NickName as Location
            if (EstablishmentPicker.SelectedItem is Establishment selected)
                Event.Location = selected.NickName;

            Completion.TrySetResult(Event);
            await CloseAsync();
        }

        private async void OnCancelClicked(object sender, System.EventArgs e)
        {
            Completion.TrySetResult(null);
            await CloseAsync();
        }
    }
}
