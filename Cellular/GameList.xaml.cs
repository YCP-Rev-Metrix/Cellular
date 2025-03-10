using System;
using Microsoft.Maui.Storage;
using Cellular.ViewModel;
using System.Collections.ObjectModel;

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
            PopulateList();
        }

        private async void OnGameClicked(object sender, EventArgs e)
        {
            await Navigation.PushAsync(new GameInterface());

        }

        void PopulateList()
        {
            foreach (string x in viewModel.Sessions)
            {
                Button sessionButton = new Button { Text = x };
                sessionButton.Clicked += OnSessionClicked;
                _sessionlist.Children.Add(sessionButton);
                StackLayout sessiongames = new StackLayout { IsVisible = false, Padding = 20 };
                sessionGames.Add(x, sessiongames);

                foreach ((string session, string gamename) y in viewModel.Games)
                {
                    if (y.session == x)
                    {
                        Button gamebutton = new Button { Text = y.gamename };
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
