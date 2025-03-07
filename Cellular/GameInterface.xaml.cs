using System;
using Microsoft.Maui.Storage;
using Cellular.ViewModel;

namespace Cellular
{
    public partial class GameInterface : ContentPage
    {

        private readonly GameInterfaceViewModel viewModel;
        public GameInterface()
        {
            InitializeComponent();
            viewModel = new GameInterfaceViewModel();
            BindingContext = viewModel;
        }
    
    }

}
