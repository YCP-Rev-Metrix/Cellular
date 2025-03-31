using System;
using Microsoft.Maui.Storage;
using Cellular.ViewModel;
using System.Collections.ObjectModel;
using System.Diagnostics;

namespace Cellular
{
    public partial class SessionList : ContentPage
    {
        private readonly SessionListViewModel viewModel;
        private Dictionary<string, StackLayout> sessionGames;
        public SessionList()
        {
            InitializeComponent();
            viewModel = new SessionListViewModel();
            BindingContext = viewModel;
            sessionGames = new Dictionary<string, StackLayout>();
            LoadDataAsync();
        }

        private async void LoadDataAsync()
        {
            await viewModel.loadSessions();
            await viewModel.loadGames();
            PopulateList();
        }

        private async void OnGameClicked(object sender, EventArgs e)
        {
            await Navigation.PushAsync(new ShotPage());
        }

        void PopulateList()
        {
            Debug.WriteLine("Populating list");
            Debug.WriteLine($"Number of sessions: {viewModel.Sessions.Count}");
            foreach (Session session in viewModel.Sessions)
            {
                Debug.WriteLine($"Session ID: {session.SessionId}, Session Number: {session.SessionNumber}");
                Button sessionButton = new Button { Text = session.SessionId.ToString() };
                sessionButton.Clicked += OnSessionClicked;
                _sessionlist.Children.Add(sessionButton);
                StackLayout sessiongames = new StackLayout { IsVisible = false, Padding = 20 };
                sessionGames.Add(session.SessionId.ToString(), sessiongames);

                foreach (Game game in viewModel.Games)
                {
                    Debug.WriteLine("This is the Game ID" + game.GameId + "and this is the Session ID" + game.Session);

                    if (game.Session == session.SessionId)
                    {
                        Debug.WriteLine("Added game to session list");
                        Button gamebutton = new Button { Text = game.GameId.ToString() };
                        gamebutton.Clicked += new EventHandler(OnGameClicked);
                        sessiongames.Children.Add(gamebutton);
                    }
                }
                _sessionlist.Children.Add(sessiongames);
            }
        }

        private async void OnSessionClicked(object sender, EventArgs e)
        {
            Button buttonSender = (Button)sender;
            string sessionName = buttonSender.Text;
            StackLayout targetLayout;
            sessionGames.TryGetValue(sessionName, out targetLayout);
            targetLayout.IsVisible = !targetLayout.IsVisible;
        }
    }

}
