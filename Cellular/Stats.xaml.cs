using Camera.MAUI;
using Cellular.Data;
using Cellular.ViewModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using System;
using System.IO;

namespace Cellular
{
    public partial class Stats : ContentPage
    {
        private readonly UserRepository _userRepository;
        private readonly MainViewModel _viewModel;

        public Stats(UserRepository userRepository)
        {
            InitializeComponent();
        }

    }
}