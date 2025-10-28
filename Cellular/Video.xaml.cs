using Camera.MAUI;
using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Storage;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using System;
using System.IO;

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

                    // Start the camera preview
                    await cameraView.StartCameraAsync(previewResolution);
                });
            }
        }

        private async void OnRecordClicked(object sender, EventArgs e)
        {
            // 1. Check if the camera is ready
            if (!isCameraStarted)
            {
                await DisplayAlert("Error", "Camera is not yet ready.", "OK");
                return;
            }

            if (!isRecording)
            {
                try
                {
                    // Update state and UI first
                    isRecording = true;
                    Record.Text = "Stop";
                    Record.BackgroundColor = Colors.Red; // Make button red for recording

                    // Define where to save the video
                    string targetFolder = Path.Combine(FileSystem.AppDataDirectory, "MyVideos");

                    // Create the directory if it doesn't exist
                    Directory.CreateDirectory(targetFolder);

                    // Create a unique filename and the full path
                    string fileName = $"rm_{DateTime.Now:yyyyMMdd_HHmmss}.mp4";
                    currentVideoPath = Path.Combine(targetFolder, fileName);

                    // Start recording
                    await cameraView.StartRecordingAsync(currentVideoPath);
                }
                catch (Exception ex)
                {
                    await DisplayAlert("Error", $"Failed to start recording: {ex.Message}", "OK");

                    // Reset button state
                    isRecording = false;
                    Record.Text = "Record";
                    Record.BackgroundColor = Color.FromArgb("#9880e5");
                }
            }
            else
            {
                try
                {
                    // Update state and UI
                    isRecording = false;
                    Record.Text = "Record";
                    Record.BackgroundColor = Color.FromArgb("#9880e5");

                    // Stop recording
                    CameraResult result = await cameraView.StopRecordingAsync();

                    if (result == CameraResult.Success)
                    {
                        if (File.Exists(currentVideoPath))
                        {
                            try
                            {
                                using var videoStream = File.OpenRead(currentVideoPath);

                                string fileName = Path.GetFileName(currentVideoPath);

                                string initialPath = App.LastSavePath;

                                var saveResult = await FileSaver.Default.SaveAsync(initialPath, fileName, videoStream, CancellationToken.None);

                                if (saveResult.IsSuccessful)
                                {
                                    App.LastSavePath = Path.GetDirectoryName(saveResult.FilePath);

                                    await DisplayAlert("Success", $"Video saved to: {saveResult.FilePath}", "OK");

                                    File.Delete(currentVideoPath);
                                }
                                else
                                {
                                    await DisplayAlert("Error", $"Failed to save to gallery: {saveResult.Exception?.Message}", "OK");
                                }
                            }
                            catch (Exception saveEx)
                            {
                                await DisplayAlert("Error", $"Error preparing to save: {saveEx.Message}", "OK");
                            }
                        }
                        else
                        {
                            await DisplayAlert("Error", "Could not find the recorded video file to save.", "OK");
                        }
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