// csharp
using System;
using System.Linq;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using Microsoft.Maui.Controls;
using Microsoft.Maui.ApplicationModel;

namespace Cellular
{
    public partial class APItestPage : ContentPage
    {
        readonly ObservableCollection<string> requestTypeOptions = new ObservableCollection<string> { "GET", "POST" };
        readonly ObservableCollection<string> objectTypeOptions = new ObservableCollection<string> { "Shot", "Session", "Game", "Frame", "Event", "Establishment", "Ball" };

        public APItestPage()
        {
            InitializeComponent();
            RequestTypeCollection.ItemsSource = requestTypeOptions;
            ObjectTypeCollection.ItemsSource = objectTypeOptions;
        }

        void OnRequestTypeToggleClicked(object sender, EventArgs e)
        {
            RequestTypeCollection.IsVisible = !RequestTypeCollection.IsVisible;
        }

        void OnObjectTypeToggleClicked(object sender, EventArgs e)
        {
            ObjectTypeCollection.IsVisible = !ObjectTypeCollection.IsVisible;
        }

        void OnRequestTypeSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var sel = RequestTypeCollection.SelectedItems.Cast<string>().ToList();
            RequestTypeToggle.Text = sel.Any() ? string.Join(", ", sel) : "Select request types";
        }

        void OnObjectTypeSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var sel = ObjectTypeCollection.SelectedItems.Cast<string>().ToList();
            ObjectTypeToggle.Text = sel.Any() ? string.Join(", ", sel) : "Select object types";
        }

        // Updated handler: asynchronous, awaits ExecuteRequest and shows responses
        async void OnLoadTestDataClicked(object sender, EventArgs e)
        {
            var button = sender as Button;
            if (button != null)
                button.IsEnabled = false;

            try
            {
                var requestSelections = RequestTypeCollection.SelectedItems.Cast<string>().ToList();
                var objectSelections = ObjectTypeCollection.SelectedItems.Cast<string>().ToList();

                if (!requestSelections.Any() || !objectSelections.Any())
                {
                    await DisplayAlert("Info", "Please select request and object types.", "OK");
                    return;
                }

                var parsedRequestTypes = new List<Cellular.Cloud_API.OperationType>();
                foreach (var s in requestSelections)
                {
                    if (Enum.TryParse<Cellular.Cloud_API.OperationType>(s, ignoreCase: true, out var rt))
                        parsedRequestTypes.Add(rt);
                }

                var parsedObjectTypes = new List<Cellular.Cloud_API.EntityType>();
                foreach (var s in objectSelections)
                {
                    if (Enum.TryParse<Cellular.Cloud_API.EntityType>(s, ignoreCase: true, out var ot))
                        parsedObjectTypes.Add(ot);
                }

                if (!parsedRequestTypes.Any() || !parsedObjectTypes.Any())
                {
                    await DisplayAlert("Info", "Failed to parse selections to enums.", "OK");
                    return;
                }

                var controller = new Cellular.Cloud_API.ApiController();

                // call ExecuteRequest for each selected combination, await result and show it
                foreach (var rt in parsedRequestTypes)
                {
                    foreach (var ot in parsedObjectTypes)
                    {
                        string? result = await controller.ExecuteRequest(
                            ot,
                            rt,
                            new List<object>(),
                            -1,
                            onAuthResponse: authResp =>
                            {
                                // marshal to UI thread and show auth response
                                MainThread.BeginInvokeOnMainThread(() =>
                                {
                                    _ = DisplayAlert("Auth Response", authResp ?? "No auth response", "OK");
                                });
                            }
                        );

                        // show the returned response from ExecuteRequest
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            _ = DisplayAlert("API Response", result ?? "No response", "OK");
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", ex.Message, "OK");
            }
            finally
            {
                if (button != null)
                    button.IsEnabled = true;
            }
        }
    }
}
