using Cellular.Data;
using Cellular.Services;
using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Extensions;
using CommunityToolkit.Maui.Storage;
using CommunityToolkit.Maui.Views;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using Plugin.BLE;
using Plugin.BLE.Abstractions.Contracts;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static Microsoft.Maui.ApplicationModel.Permissions;

namespace Cellular
{
    public partial class Video2 : ContentPage
    {
        private readonly List<Size> CommonResolutions = new List<Size>
        {
            new Size(3840, 2160), // 4K UHD
            new Size(2560, 1440), // 1440p / QHD
            new Size(1920, 1080), // 1080p / Full HD
            new Size(1280, 720),  // 720p / HD
            new Size(854, 480)    // 480p / FWVGA
        };

        private bool isCameraStarted = false;
        private Size previewResolution = new Size(1920, 1080);

        //Recording state
        private bool isRecording = false;
        private string currentVideoPath = string.Empty;
        private System.IO.FileStream? _videoFileStream;

        // Watch BLE service for external recording control
        private IWatchBleService _watchBleService;

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await Permissions.RequestAsync<Permissions.Camera>();
            await Permissions.RequestAsync<Permissions.Microphone>();
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();

            // Unsubscribe from watch recording events
            _watchBleService.WatchStartRecordingRequested -= async (s, e) => await BeginExternalRecording();
            _watchBleService.WatchStopRecordingRequested -= async (s, e) => await EndExternalRecording();

            // Clean up stream if recording
            CleanupStream();
        }
        
        public Video2()
        {
            InitializeComponent();

            // Get MetaWear service from dependency injection
            _watchBleService = Handler?.MauiContext?.Services?.GetService<IWatchBleService>()
                ?? Application.Current?.Handler?.MauiContext?.Services?.GetService<IWatchBleService>()
                ?? Microsoft.Maui.Controls.Application.Current?.Handler?.MauiContext?.Services?.GetService<IWatchBleService>()
                ?? new WatchBleService(); // Last resort - but this should not happen with proper DI

            // Subscribe to watch recording commands
            _watchBleService.WatchStartRecordingRequested += async (s, e) => await BeginExternalRecording();
            _watchBleService.WatchStopRecordingRequested += async (s, e) => await EndExternalRecording();

            cameraView.SizeChanged += CameraView_SizeChanged;

            // Use PropertyChanged to react to SelectedCamera changes (CommunityToolkit CameraView)
            cameraView.PropertyChanged += CameraView_PropertyChanged;
            ResolutionPicker.SelectedIndexChanged += ResolutionPicker_SelectedIndexChanged;
        }

        private void CameraView_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(cameraView.SelectedCamera))
            {
                var sel = cameraView.SelectedCamera;
                if (sel?.SupportedResolutions != null)
                {
                    // Filter: Only keep resolutions that exist in your 'CommonResolutions' list
                    var filteredResolutions = sel.SupportedResolutions
                        .Where(s => CommonResolutions.Any(common =>
                            (int)common.Width == (int)s.Width &&
                            (int)common.Height == (int)s.Height))
                        .OrderByDescending(s => s.Width) // Keep highest quality at the top
                        .ToList();

                    // If no common resolutions match, just show all of them
                    ResolutionPicker.ItemsSource = filteredResolutions.Any()
                        ? filteredResolutions
                        : sel.SupportedResolutions.ToList();

                    ResolutionPicker.ItemDisplayBinding = new Binding(".", stringFormat: "{0.Width}x{0.Height}");

                    // Try to find 1080p
                    var defaultRes = filteredResolutions.FirstOrDefault(s => Math.Abs(s.Width - 1920) < 10);

                    // If defaultRes is "Empty" (0x0), take the first available in the filtered list
                    if (defaultRes.Width == 0 && filteredResolutions.Any())
                    {
                        defaultRes = filteredResolutions.First();
                    }

                    // Assign to the Picker
                    ResolutionPicker.SelectedItem = defaultRes;
                }
            }
        }
        private async void ResolutionPicker_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (ResolutionPicker.SelectedItem is Size size)
            {
                previewResolution = size;
                // apply to camera for captures
                cameraView.ImageCaptureResolution = size;
                // no need to stop/start preview; preview may remain unchanged
            }
        }
        private void CameraView_SizeChanged(object sender, EventArgs e)
        {
            if (cameraView != null && !isCameraStarted && cameraView.Width > 0 && cameraView.Height > 0)
            {
                isCameraStarted = true;

                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    //Delay is placed here so that the camera goes to the correct resolution
                    await Task.Delay(100);

                    // Start the camera preview
                    try
                    {
                        //await cameraView.StartCameraPreview(CancellationToken.None);
                        try { cameraView.ImageCaptureResolution = previewResolution; } catch { }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"StartCameraPreview failed: {ex.Message}");
                    }
                });
            }
        }
        private async void OnRecordClicked(object sender, EventArgs e)
        {
            if (!isRecording)
            {
                try
                {
                    // Prepare Folder and Path
                    string targetFolder = Path.Combine(FileSystem.CacheDirectory, "Videos");
                    Directory.CreateDirectory(targetFolder);
                    string fileName = $"rm_{DateTime.Now:yyyyMMdd_HHmmss}.mp4";
                    currentVideoPath = Path.Combine(targetFolder, fileName);

                    // Open Stream (Use Create to overwrite if exists)
                    _videoFileStream = File.Create(currentVideoPath);

                    // Start Recording
                    // Note: CancellationToken.None is fine for starting
                    await cameraView.StartVideoRecording(_videoFileStream, CancellationToken.None);

                    isRecording = true;
                    RecordBtn.Text = "Stop Recording";
                    RecordBtn.BackgroundColor = Colors.Red;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Start failed: {ex.Message}");
                    CleanupStream();
                }
            }
            else
            {
                try
                {
                    // 4. Stop Recording first
                    await cameraView.StopVideoRecording(CancellationToken.None);

                    // 5. Cleanup the stream to flush data to disk
                    CleanupStream();

                    // 6. Save to Public Gallery/Folder
                    await SaveVideoToGallery();

                    isRecording = false;
                    RecordBtn.Text = "Record";
                    RecordBtn.BackgroundColor = Colors.DeepSkyBlue;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Stop failed: {ex.Message}");
                }
            }
        }

        private void CleanupStream()
        {
            if (_videoFileStream != null)
            {
                _videoFileStream.Flush();
                _videoFileStream.Dispose();
                _videoFileStream = null;
            }
        }

        private async Task SaveVideoToGallery()
        {
            try
            {
                if (File.Exists(currentVideoPath))
                {
                    using var fileReadStream = File.OpenRead(currentVideoPath);
                    var result = await FileSaver.Default.SaveAsync(Path.GetFileName(currentVideoPath), fileReadStream, CancellationToken.None);

                    if (result.IsSuccessful)
                        await DisplayAlertAsync("Success", "Video saved to your device", "OK");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Save failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Starts video recording when triggered by watch external command
        /// </summary>
        public async Task BeginExternalRecording()
        {
            if (!isCameraStarted || isRecording)
                return;

            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                try
                {
                    // Prepare Folder and Path
                    string targetFolder = Path.Combine(FileSystem.CacheDirectory, "Videos");
                    Directory.CreateDirectory(targetFolder);
                    string fileName = $"rm_{DateTime.Now:yyyyMMdd_HHmmss}.mp4";
                    currentVideoPath = Path.Combine(targetFolder, fileName);

                    // Open Stream (Use Create to overwrite if exists)
                    _videoFileStream = File.Create(currentVideoPath);

                    // Start Recording
                    await cameraView.StartVideoRecording(_videoFileStream, CancellationToken.None);

                    isRecording = true;
                    RecordBtn.Text = "Stop Recording";
                    RecordBtn.BackgroundColor = Colors.Red;

                    System.Diagnostics.Debug.WriteLine("[Video2] External recording started from watch command");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Video2] BeginExternalRecording failed: {ex.Message}");
                    CleanupStream();
                    isRecording = false;
                    RecordBtn.Text = "Record";
                    RecordBtn.BackgroundColor = Colors.DeepSkyBlue;
                }
            });
        }

        /// <summary>
        /// Stops video recording when triggered by watch external command
        /// </summary>
        public async Task EndExternalRecording()
        {
            if (!isRecording)
                return;

            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                try
                {
                    // Stop Recording
                    await cameraView.StopVideoRecording(CancellationToken.None);

                    // Cleanup the stream to flush data to disk
                    CleanupStream();

                    // Save to Public Gallery/Folder
                    await SaveVideoToGallery();

                    isRecording = false;
                    RecordBtn.Text = "Record";
                    RecordBtn.BackgroundColor = Colors.DeepSkyBlue;

                    System.Diagnostics.Debug.WriteLine("[Video2] External recording stopped from watch command");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Video2] EndExternalRecording failed: {ex.Message}");
                }
            });
        }
    }
}