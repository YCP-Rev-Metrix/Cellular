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

        DatePicker datePicker = new DatePicker
        {
            MinimumDate = new DateTime(1900, 1, 1),
            MaximumDate = new DateTime(2050, 1, 1),
            Date = new DateTime(2018, 6, 21)
        };
    }
}