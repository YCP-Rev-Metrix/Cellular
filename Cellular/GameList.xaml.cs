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

        private async void OnGameClicked(object sender, EventArgs e,int gameID, int gameNumber)
        {
            Preferences.Set("GameNumber", gameNumber);
            Preferences.Set("GameID", gameID);
            await Navigation.PushAsync(new ShotPage());
        }

        private async void GoToShot(object sender, EventArgs e)
        {
            await Navigation.PushAsync(new ShotPage());
        }

        void PopulateList()
        {
            sessionGames.Clear();
            _sessionlist.Children.Clear(); // Clear all buttons
            Debug.WriteLine("Populating list");
            Debug.WriteLine($"Number of sessions: {viewModel.Sessions.Count}");
            foreach (Session session in viewModel.Sessions)
            {
                Debug.WriteLine($"Session ID: {session.SessionId}, Session Number: {session.SessionNumber}");
                Button sessionButton = new Button { Text = "Session " + session.SessionNumber.ToString() };
                sessionButton.Clicked += (sender, e) => OnSessionClicked(sender, e, session.SessionId, session.SessionNumber);
                _sessionlist.Children.Add(sessionButton);
                StackLayout sessiongames = new StackLayout { IsVisible = false, Padding = 20 };
                sessionGames.Add(session.SessionId.ToString(), sessiongames);
                Debug.WriteLine($"Number of Games: {viewModel.Games.Count}");

                foreach (Game game in viewModel.Games)
                {
                    Debug.WriteLine("This is the Game ID" + game.GameId + "and this is the Session ID" + game.SessionId);

                    if (game.SessionId == session.SessionId)
                    {
                        Debug.WriteLine("Added game to session list");
                        Button gamebutton = new Button { Text = "Game " + game.GameNumber.ToString() };
                        gamebutton.Clicked += (sender, e) => OnGameClicked(sender, e, game.GameId, game?.GameNumber ?? 0);
                        sessiongames.Children.Add(gamebutton);
                    }

                    
                }
                Button AddGameButton = new Button { Text = "Add Game" };
                AddGameButton.Clicked += (sender,e) => AddGame(sender,e,session.SessionId);
                sessiongames.Children.Add(AddGameButton);

                _sessionlist.Children.Add(sessiongames);
            }
        }

        private async void AddGame(object sender, EventArgs e, int session)
        {
            Debug.WriteLine("THIS IS THE SESSION NUMBER" + session + "------------------------------------------------------------------");
            await viewModel.AddGame(session);
            LoadDataAsync();
        }
        private async void AddSession(object sender, EventArgs e)
        {
            await viewModel.AddSession();
            LoadDataAsync();
        }
        private async void OnSessionClicked(object sender, EventArgs e, int sessionId, int sessionNumber)
        {
            Preferences.Set("CurrentSession", sessionId);
            Preferences.Set("SessionNumber", sessionNumber);
            StackLayout targetLayout;
            sessionGames.TryGetValue(sessionId.ToString(), out targetLayout);
            if (targetLayout != null)
            {
                targetLayout.IsVisible = !targetLayout.IsVisible;
            }
        }
    }

}
