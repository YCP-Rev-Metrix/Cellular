using System;
using System.IO;
using Camera.MAUI;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;

namespace Cellular
{
    public partial class Video : ContentPage
    {
        private bool isCameraStarted = false;
        private Size previewResolution = new Size(1920, 1080); // 1080p
        //private Size previewResolution = new Size(1280, 720); // 720p
        //private Size previewResolution = new Size(3840, 2160); // 4K

        //Recording state
        private bool isRecording = false;
        private string currentVideoPath;

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await Permissions.RequestAsync<Permissions.Camera>();
            await Permissions.RequestAsync<Permissions.Microphone>();
        }
        public Video()
        {
            InitializeComponent();

            cameraView.CamerasLoaded += CameraView_CamerasLoaded;
            cameraView.SizeChanged += CameraView_SizeChanged;
        }

        private void CameraView_CamerasLoaded(object sender, EventArgs e)
        {
            if (cameraView.Cameras.Count > 0)
            {
                //Select camera
                cameraView.Camera = cameraView.Cameras.FirstOrDefault(c => c.Position == Camera.MAUI.CameraPosition.Back);
            }
        }

        private void CameraView_SizeChanged(object sender, EventArgs e)
        {
            if (cameraView.Camera != null && !isCameraStarted && cameraView.Width > 0 && cameraView.Height > 0)
            {
                isCameraStarted = true;

                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    //Delay is placed here so that the camera goes to the correct resolution
                    await Task.Delay(100);

                    // Start the camera preview now
                    await cameraView.StartCameraAsync(previewResolution);
                });
            }
        }

        private async void OnRecordClicked(object sender, EventArgs e)
        {
            if (!isCameraStarted)
            {
                await DisplayAlert("Error", "Camera is not yet ready.", "OK");
                return;
            }

            if (!isRecording)
            {
                // Start Recording
                try
                {
                    isRecording = true;
                    Record.Text = "Stop";
                    Record.BackgroundColor = Colors.Red; //Make button red for recording

                    // Define where to save the video. We use the cache directory.
                    currentVideoPath = Path.Combine(FileSystem.CacheDirectory, $"{Guid.NewGuid()}.mp4");

                    // Start recording
                    await cameraView.StartRecordingAsync(currentVideoPath);
                }
                catch (Exception ex)
                {
                    await DisplayAlert("Error", $"Failed to start recording: {ex.Message}", "OK");
                    isRecording = false; // Reset button state
                    Record.Text = "Record";
                    Record.BackgroundColor = Colors.Red;
                }
            }
            else
            {
                // Stop Recording
                try
                {
                    isRecording = false;
                    Record.Text = "Record";
                    Record.BackgroundColor = Colors.Red;

                    // Stop recording
                    CameraResult result = await cameraView.StopRecordingAsync();

                    if (result == CameraResult.Success)
                    {
                        await DisplayAlert("Success", $"Video saved to: {currentVideoPath}", "OK");
                    }
                    else
                    {
                        await DisplayAlert("Error", "Failed to save the video.", "OK");
                    }
                }
                catch (Exception ex)
                {
                    await DisplayAlert("Error", $"Failed to stop recording: {ex.Message}", "OK");
                }
            }
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();

            isCameraStarted = false;

            cameraView.StopCameraAsync();
        }
    }
}