﻿using System;
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

        public void boardChanged(object sender, EventArgs e)
        {
            Slider typedSender = (Slider)sender;
            if (typedSender.Value == 0 || typedSender.Value == 40)
            {
                TestingLabel.Text = "Gutter";
            }
            else
            {
                TestingLabel.Text = typedSender.Value.ToString();
            }
        }
    
    }

}
