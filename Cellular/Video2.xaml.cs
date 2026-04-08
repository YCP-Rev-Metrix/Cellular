using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Storage;
using CommunityToolkit.Maui.Views;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using System;
using System.IO;
using Cellular.Services;
using Cellular.Data;
using Plugin.BLE;
using Plugin.BLE.Abstractions.Contracts;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Maui.Extensions;

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

        // Stop video only after log is saved; timeout if no log save happens
        private CancellationTokenSource? _stopVideoCts;

        // Upload to RevMetrix API (Digital Ocean Space) - use your API credentials
        private static readonly string RevMetrixApiUsername = "string";
        private static readonly string RevMetrixApiPassword = "string";

        // Sensor data buffering
        private SensorBufferManager? _sensorBufferManager;
        private IMetaWearService _metaWearService; // Not readonly so we can update to singleton if needed
        private readonly UserRepository _userRepository;
        private readonly IBluetoothLE _ble = CrossBluetoothLE.Current;
        private readonly IAdapter _adapter = CrossBluetoothLE.Current.Adapter;

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await Permissions.RequestAsync<Permissions.Camera>();
            await Permissions.RequestAsync<Permissions.Microphone>();

            // Ensure we have the singleton service instance (Handler is available in OnAppearing)
            if (Handler?.MauiContext?.Services != null)
            {
                var serviceFromDI = Handler.MauiContext.Services.GetService<IMetaWearService>();
                if (serviceFromDI != null && serviceFromDI != _metaWearService)
                {
                    // We got a different instance, update our reference to the singleton
                    _metaWearService.DeviceDisconnected -= OnDeviceDisconnected;
                    _metaWearService = serviceFromDI;
                }
            }

            // Re-subscribe to events in case page was recreated
            _metaWearService.DeviceDisconnected -= OnDeviceDisconnected; // Remove first to avoid duplicates
            _metaWearService.DeviceDisconnected += OnDeviceDisconnected;

            // Recreate sensor buffer manager if it was disposed when the page last disappeared
            if (_sensorBufferManager == null)
            {
                _sensorBufferManager = new SensorBufferManager(_metaWearService);
                _sensorBufferManager.DataSaved += OnSensorDataSaved;
                _sensorBufferManager.SaveError += OnSensorSaveError;
                _sensorBufferManager.ContinuousSaveStarted += OnContinuousSaveStarted;
                _sensorBufferManager.ContinuousSaveComplete += OnContinuousSaveComplete;
            }

            // Check actual connection status and update icon and record button
            // The service is a singleton, so it maintains connection state
            UpdateConnectionStatusIcon();
            await UpdateRecordButtonStateAsync();
        }

        public Video2()
        {
            InitializeComponent();

            // Get MetaWear service from dependency injection
            // Try multiple ways to get the singleton instance
            _metaWearService = Handler?.MauiContext?.Services?.GetService<IMetaWearService>()
                ?? Application.Current?.Handler?.MauiContext?.Services?.GetService<IMetaWearService>()
                ?? Microsoft.Maui.Controls.Application.Current?.Handler?.MauiContext?.Services?.GetService<IMetaWearService>()
                ?? new MetaWearBleService(); // Last resort - but this should not happen with proper DI

            // Initialize UserRepository
            _userRepository = new UserRepository(new CellularDatabase().GetConnection());

            // Subscribe to connection/disconnection events
            _metaWearService.DeviceDisconnected -= OnDeviceDisconnected; // Remove first to avoid duplicates
            _metaWearService.DeviceDisconnected += OnDeviceDisconnected;

            // Initialize sensor buffer manager
            _sensorBufferManager = new SensorBufferManager(_metaWearService);
            _sensorBufferManager.DataSaved += OnSensorDataSaved;
            _sensorBufferManager.SaveError += OnSensorSaveError;
            _sensorBufferManager.ContinuousSaveStarted += OnContinuousSaveStarted;
            _sensorBufferManager.ContinuousSaveComplete += OnContinuousSaveComplete;

            cameraView.PropertyChanged += CameraView_PropertyChanged;
            cameraView.SizeChanged += CameraView_SizeChanged;

            // Don't update icon here - wait for OnAppearing when service is definitely ready
            //MessagingCenter.Subscribe<object>(this, "WatchStartRecording", async (_) =>
            //{
            //    await BeginExternalRecording();
            //});

            //MessagingCenter.Subscribe<object>(this, "WatchStopRecording", async (_) =>
            //{
            //    await EndExternalRecording();
            //});
        }

        private void CameraView_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(cameraView.SelectedCamera))
            {
                var sel = cameraView.SelectedCamera;
                if (sel != null)
                    isCameraStarted = true;

                if (sel?.SupportedResolutions != null)
                {
                    // Filter to only common resolutions
                    var supportedResolutions = sel.SupportedResolutions
                        .Where(available => CommonResolutions.Any(common =>
                            (int)common.Width == (int)available.Width && (int)common.Height == (int)available.Height))
                        .OrderByDescending(s => s.Width * s.Height)
                        .ToList();

                    // Set the display format for the Picker items
                    ResolutionPicker.ItemDisplayBinding = new Binding(".", stringFormat: "{0.Width}x{0.Height}");

                    ResolutionPicker.ItemsSource = supportedResolutions.Any()
                        ? supportedResolutions
                        : sel.SupportedResolutions.ToList();

                    // Try to find and select the default resolution (1080p)
                    var defaultResolution = supportedResolutions
                        .FirstOrDefault(s => Math.Abs(s.Width - 1920) < 10);

                    if (defaultResolution.Width != 0)
                    {
                        ResolutionPicker.SelectedItem = defaultResolution;
                    }
                    else if (supportedResolutions.Any())
                    {
                        // If 1080p isn't available, select the first (highest) available resolution
                        ResolutionPicker.SelectedIndex = 0;
                    }
                }
            }
        }

        private void ResolutionPicker_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (ResolutionPicker.SelectedItem is Size selectedSize)
            {
                // Update the internal resolution field
                previewResolution = selectedSize;

                // Apply to the camera view capture resolution
                try { cameraView.ImageCaptureResolution = selectedSize; } catch { }
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

                    // Apply the capture resolution
                    try { cameraView.ImageCaptureResolution = previewResolution; } catch { }
                });
            }
        }

        private async void OnRecordClicked(object sender, EventArgs e)
        {
            if (!isRecording)
            {
                try
                {
                    // Update state and UI first
                    isRecording = true;
                    RecordBtn.Text = "Stop";
                    RecordBtn.BackgroundColor = Colors.Red; // Make button red for recording

                    // Define where to save the video
                    string targetFolder = Path.Combine(FileSystem.AppDataDirectory, "MyVideos");

                    // Create the directory if it doesn't exist
                    Directory.CreateDirectory(targetFolder);

                    // Create a unique filename and the full path
                    string fileName = $"rm_{DateTime.Now:yyyyMMdd_HHmmss}.mp4";
                    currentVideoPath = Path.Combine(targetFolder, fileName);

                    // Start sensor data buffering with video filename
                    if (_sensorBufferManager != null)
                    {
                        string baseFileName = Path.GetFileNameWithoutExtension(fileName);
                        await _sensorBufferManager.StartBufferingAsync(baseFileName);
                    }

                    // Start recording
                    _videoFileStream = File.Create(currentVideoPath);
                    await cameraView.StartVideoRecording(_videoFileStream, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    await DisplayAlert("Error", $"Failed to start recording: {ex.Message}", "OK");

                    // Reset button state
                    isRecording = false;
                    RecordBtn.Text = "Record";
                    RecordBtn.BackgroundColor = Color.FromArgb("#9880e5");

                    // Stop buffering if it was started
                    if (_sensorBufferManager != null)
                    {
                        await _sensorBufferManager.StopBufferingAsync();
                    }

                    CleanupStream();
                }
            }
            else
            {
                // User clicked Stop: stop sensor buffering first. Video keeps recording until log is saved (or timeout).
                try
                {
                    _stopVideoCts?.Cancel();
                    _stopVideoCts = new CancellationTokenSource();
                    var cts = _stopVideoCts;

                    if (_sensorBufferManager != null)
                        await _sensorBufferManager.StopBufferingAsync();

                    // If we get OnContinuousSaveComplete, we'll stop the video there and upload both. Otherwise after 5s stop video and upload video only.
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await Task.Delay(5000, cts.Token);
                        }
                        catch (OperationCanceledException)
                        {
                            return;
                        }
                        MainThread.BeginInvokeOnMainThread(async () =>
                        {
                            if (!isRecording) return;
                            await StopVideoAndUploadVideoOnlyAsync();
                        });
                    });
                }
                catch (Exception ex)
                {
                    await DisplayAlert("Error", $"Failed to stop recording: {ex.Message}", "OK");
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

        /// <summary>
        /// Stops the video and uploads only the video (no log). Used when log save never completed (timeout).
        /// </summary>
        private async Task StopVideoAndUploadVideoOnlyAsync()
        {
            if (!isRecording || string.IsNullOrEmpty(currentVideoPath)) return;
            try
            {
                isRecording = false;
                RecordBtn.Text = "Record";
                RecordBtn.BackgroundColor = Color.FromArgb("#9880e5");
                await cameraView.StopVideoRecording(CancellationToken.None);
                CleanupStream();
                if (File.Exists(currentVideoPath))
                    await SaveAndUploadVideoAsync(currentVideoPath, logBytesForUpload: null, logFileNameForUpload: null, folderToSaveVideoTo: null);
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Failed to stop/upload video: {ex.Message}", "OK");
            }
        }

        /// <summary>
        /// Saves video to the phone (same folder as log when provided), uploads video and log to RevMetrix API.
        /// </summary>
        /// <param name="videoPath">Temp video file path (will be deleted after upload).</param>
        /// <param name="logBytesForUpload">Log file content for upload (from app cache); avoids Android access-denied to user path.</param>
        /// <param name="logFileNameForUpload">Filename for the log upload (e.g. sensor_xxx.json).</param>
        /// <param name="folderToSaveVideoTo">Folder to save video on phone (same as log); we use FileSaver so the video is actually written on all platforms.</param>
        private async Task SaveAndUploadVideoAsync(string videoPath, byte[]? logBytesForUpload, string? logFileNameForUpload, string? folderToSaveVideoTo)
        {
            // Save video to the phone using FileSaver (same folder as log when provided). On Android, folder path may be denied — retry with no initial path.
            string? savedVideoPath = null;
            string? saveFolder = folderToSaveVideoTo ?? App.LastSavePath;
            if (File.Exists(videoPath))
            {
                string videoFileName = Path.GetFileName(videoPath);
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    try
                    {
                        using var videoStream = File.OpenRead(videoPath);
                        var initialPath = attempt == 0 ? (saveFolder ?? "") : "";
                        var saveResult = await FileSaver.Default.SaveAsync(initialPath, videoFileName, videoStream, CancellationToken.None);
                        if (saveResult.IsSuccessful)
                        {
                            savedVideoPath = saveResult.FilePath;
                            if (!string.IsNullOrEmpty(savedVideoPath))
                                App.LastSavePath = Path.GetDirectoryName(savedVideoPath);
                        }
                        break;
                    }
                    catch (Exception ex) when (attempt == 0 && (ex.Message.Contains("denied", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("access", StringComparison.OrdinalIgnoreCase)))
                    {
                        continue; // Retry with empty initial path so user picks location
                    }
                    catch (Exception ex)
                    {
                        await DisplayAlert("Save Video", $"Video will upload to cloud but could not save to phone: {ex.Message}", "OK");
                        break;
                    }
                }
            }

            var uploadService = new RevMetrixUploadService();
            try
            {
                string? token = await uploadService.GetTokenAsync(RevMetrixApiUsername, RevMetrixApiPassword);
                if (string.IsNullOrEmpty(token))
                {
                    await DisplayAlert("Upload Error", "Could not get API token.", "OK");
                    return;
                }
                string videoKey = await uploadService.UploadFileAsync(token, videoPath, "videos", "video/mp4");
                string? logKey = null;
                if (logBytesForUpload != null && logBytesForUpload.Length > 0 && !string.IsNullOrEmpty(logFileNameForUpload))
                {
                    try
                    {
                        logKey = await uploadService.UploadFileAsync(token, logBytesForUpload, logFileNameForUpload, "logs", "application/json");
                    }
                    catch (Exception logEx)
                    {
                        await DisplayAlert("Log Upload", $"Video uploaded successfully. Log failed to upload: {logEx.Message}", "OK");
                    }
                }
                try { File.Delete(videoPath); } catch { /* ignore */ }
                string msg = logKey != null
                    ? $"Video and log uploaded.\nVideo key: {videoKey}\nLog key: {logKey}"
                    : $"Video uploaded.\nKey: {videoKey}";
                if (!string.IsNullOrEmpty(savedVideoPath))
                    msg += $"\n\nVideo saved to phone:\n{savedVideoPath}";
                await DisplayAlert("Upload Complete", msg, "OK");
            }
            catch (Exception ex)
            {
                await DisplayAlert("Upload Failed", ex.Message, "OK");
            }
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();

            // Stop buffering if active
            if (_sensorBufferManager != null)
            {
                _ = _sensorBufferManager.StopBufferingAsync();
                _sensorBufferManager.DataSaved -= OnSensorDataSaved;
                _sensorBufferManager.SaveError -= OnSensorSaveError;
                _sensorBufferManager.ContinuousSaveStarted -= OnContinuousSaveStarted;
                _sensorBufferManager.ContinuousSaveComplete -= OnContinuousSaveComplete;
                _sensorBufferManager.Dispose();
                _sensorBufferManager = null;
            }

            // Don't unsubscribe from DeviceDisconnected here - we want to keep listening
            // The service maintains connection across page navigation

            isCameraStarted = false;

            CleanupStream();
        }

        private void OnSensorDataSaved(object? sender, SensorDataSavedEventArgs e)
        {
            // This event is no longer used - continuous save starts directly when light sensor hits threshold
            // Keeping this handler to avoid breaking the event subscription, but it won't be called
            System.Diagnostics.Debug.WriteLine($"[Video2] OnSensorDataSaved called but ignored (continuous save starts directly now)");
        }

        private void OnSensorSaveError(object? sender, string errorMessage)
        {
            // Show error notification on main thread
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await DisplayAlert("Error", $"Failed to save sensor data: {errorMessage}", "OK");
            });
        }

        private void OnContinuousSaveStarted(object? sender, EventArgs e)
        {
            // Show notification when 4-second collection starts
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await DisplayAlert("Recording Started",
                    $"4-second sensor data collection has started!\n\n" +
                    $"Data is being collected now. You will be prompted to save when collection completes.",
                    "OK");
            });
        }

        private void OnContinuousSaveComplete(object? sender, SensorDataSavedEventArgs e)
        {
            // Cancel the "stop video after 5s" timeout — we will stop the video after the log is saved.
            _stopVideoCts?.Cancel();

            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await Task.Delay(500);

                if (!File.Exists(e.FilePath))
                {
                    await DisplayAlert("Error", $"Sensor data temp file not found at:\n{e.FilePath}", "OK");
                    await StopVideoAndUploadVideoOnlyAsync();
                    return;
                }

                try
                {
                    // Read log into memory from app cache path (we have access). Upload from bytes to avoid touching user path on Android.
                    byte[]? logBytesForUpload = null;
                    string? logFileNameForUpload = null;
                    try
                    {
                        logBytesForUpload = await File.ReadAllBytesAsync(e.FilePath);
                        logFileNameForUpload = Path.GetFileName(e.FilePath);
                    }
                    catch (Exception readEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Video2] Could not read log for upload: {readEx.Message}");
                    }

                    string fileName = Path.GetFileName(e.FilePath);
                    string initialPath = App.LastSavePath;
                    using var saveStream = logBytesForUpload != null ? (Stream)new MemoryStream(logBytesForUpload) : File.OpenRead(e.FilePath);
                    var saveResult = await FileSaver.Default.SaveAsync(initialPath, fileName, saveStream, CancellationToken.None);

                    string? logSavedDirectory = null;
                    if (saveResult.IsSuccessful)
                    {
                        App.LastSavePath = Path.GetDirectoryName(saveResult.FilePath);
                        logSavedDirectory = App.LastSavePath;
                    }

                    // Stop the video recording now that the log is saved (or user cancelled)
                    if (isRecording && !string.IsNullOrEmpty(currentVideoPath))
                    {
                        isRecording = false;
                        RecordBtn.Text = "Record";
                        RecordBtn.BackgroundColor = Color.FromArgb("#9880e5");
                        await cameraView.StopVideoRecording(CancellationToken.None);
                        CleanupStream();
                        if (File.Exists(currentVideoPath))
                            await SaveAndUploadVideoAsync(currentVideoPath, logBytesForUpload, logFileNameForUpload, folderToSaveVideoTo: logSavedDirectory);
                    }

                    try { if (File.Exists(e.FilePath)) File.Delete(e.FilePath); } catch (Exception deleteEx) { System.Diagnostics.Debug.WriteLine($"[Video2] Error deleting temp log: {deleteEx.Message}"); }

                    if (saveResult.IsSuccessful && logBytesForUpload != null)
                    {
                        await DisplayAlert("Success",
                            $"Sensor data saved.\nData Points: {e.DataPointCount}\n\nVideo and log have been uploaded to RevMetrix.",
                            "OK");
                    }
                    else if (!saveResult.IsSuccessful)
                    {
                        await DisplayAlert("Error", $"Failed to save sensor data: {saveResult.Exception?.Message ?? "Unknown error"}", "OK");
                    }
                }
                catch (Exception ex)
                {
                    await DisplayAlert("Error", $"Error preparing to save sensor data: {ex.Message}", "OK");
                    if (isRecording)
                        await StopVideoAndUploadVideoOnlyAsync();
                }
            });
        }

        public async Task BeginExternalRecording()
        {
            if (!isCameraStarted || isRecording)
                return;

            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                try
                {
                    isRecording = true;
                    RecordBtn.Text = "Stop";
                    RecordBtn.BackgroundColor = Colors.Red;

                    string targetFolder = Path.Combine(FileSystem.AppDataDirectory, "MyVideos");
                    Directory.CreateDirectory(targetFolder);

                    string fileName = $"rm_{DateTime.Now:yyyyMMdd_HHmmss}.mp4";
                    currentVideoPath = Path.Combine(targetFolder, fileName);

                    _videoFileStream = File.Create(currentVideoPath);
                    await cameraView.StartVideoRecording(_videoFileStream, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    isRecording = false;
                    RecordBtn.Text = "Record";
                    RecordBtn.BackgroundColor = Color.FromArgb("#9880e5");
                    CleanupStream();
                }
            });
        }

        public async Task EndExternalRecording()
        {
            if (!isRecording)
                return;

            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                try
                {
                    // Update state and UI
                    isRecording = false;
                    RecordBtn.Text = "Record";
                    RecordBtn.BackgroundColor = Color.FromArgb("#9880e5");

                    // Stop recording
                    await cameraView.StopVideoRecording(CancellationToken.None);
                    CleanupStream();

                    if (File.Exists(currentVideoPath))
                    {
                        try
                        {
                            using var videoStream = File.OpenRead(currentVideoPath);

                            string fileName = Path.GetFileName(currentVideoPath);
                            string initialPath = App.LastSavePath;

                            var saveResult = await FileSaver.Default.SaveAsync(
                                initialPath,
                                fileName,
                                videoStream,
                                CancellationToken.None);

                            if (saveResult.IsSuccessful)
                            {
                                App.LastSavePath = Path.GetDirectoryName(saveResult.FilePath);
                                await DisplayAlert("Success", $"Video saved to: {saveResult.FilePath}", "OK");

                                File.Delete(currentVideoPath);
                            }
                            else
                            {
                                await DisplayAlert(
                                    "Error",
                                    $"Failed to save to gallery: {saveResult.Exception?.Message}",
                                    "OK");
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
                catch (Exception ex)
                {
                    await DisplayAlert("Error", $"Error stopping or saving recording: {ex.Message}", "OK");
                }
            });
        }

        private void OnDeviceDisconnected(object? sender, string macAddress)
        {
            // Update icon and record button when device disconnects
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                // Update IsConnected status in database
                await UpdateIsConnectedStatusAsync(false);

                UpdateConnectionStatusIcon();
                await UpdateRecordButtonStateAsync();
            });
        }

        private void UpdateConnectionStatusIcon()
        {
            // Update toolbar item icon color based on connection status
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (_metaWearService.IsConnected)
                {
                    // Green when connected - use FontImageSource with green color
                    MmsConnectionToolbarItem.IconImageSource = new FontImageSource
                    {
                        Glyph = "●",
                        FontFamily = "Arial",
                        Size = 20,
                        Color = Colors.Green
                    };
                }
                else
                {
                    // Red when not connected - use FontImageSource with red color
                    MmsConnectionToolbarItem.IconImageSource = new FontImageSource
                    {
                        Glyph = "●",
                        FontFamily = "Arial",
                        Size = 20,
                        Color = Colors.Red
                    };
                }
            });
        }

        private async void OnMmsConnectionIconClicked(object sender, EventArgs e)
        {
            // Check connection status first - refresh it
            bool isConnected = _metaWearService.IsConnected;
            string macAddress = _metaWearService.MacAddress ?? "Unknown";

            // If not connected, try to get saved MAC address from database
            if (!isConnected)
            {
                int userId = Preferences.Get("UserId", -1);
                if (userId != -1)
                {
                    string? savedMac = await _userRepository.GetSmartDotMacAsync(userId);
                    if (!string.IsNullOrEmpty(savedMac))
                    {
                        macAddress = savedMac;
                    }
                }
            }

            // Show popup with connection info
            var popup = new Cellular.Views.MmsConnectionPopup(macAddress, isConnected);

            // Ensure Completion is set if the popup is closed by other means
            popup.Closed += (s, args) =>
            {
                if (!popup.Completion.Task.IsCompleted)
                {
                    popup.Completion.TrySetResult(null);
                }
            };

            this.ShowPopup(popup);

            // Await the completion source that the popup sets on connect/disconnect/close
            var result = await popup.Completion.Task;

            // Handle the result
            if (result.HasValue)
            {
                if (result.Value == true)
                {
                    // User clicked Disconnect
                    await DisconnectFromMmsAsync();
                }
                else if (result.Value == false)
                {
                    // User clicked Connect
                    await TryAutoConnectToSavedDeviceAsync();
                }
            }
            else
            {
                // User clicked Close - just update icon to ensure it's correct
                UpdateConnectionStatusIcon();
            }
        }

        private async Task DisconnectFromMmsAsync()
        {
            try
            {
                // Stop all sensors first
                await _metaWearService.StopAccelerometerAsync();
                await _metaWearService.StopGyroscopeAsync();
                await _metaWearService.StopMagnetometerAsync();
                await _metaWearService.StopLightSensorAsync();

                // Disconnect from device
                await _metaWearService.DisconnectAsync();

                // Update IsConnected status in database
                await UpdateIsConnectedStatusAsync(false);

                // Update icon and record button
                UpdateConnectionStatusIcon();
                await UpdateRecordButtonStateAsync();

                await DisplayAlert("Disconnected", "Successfully disconnected from SmartDot device.", "OK");
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Failed to disconnect: {ex.Message}", "OK");
            }
        }

        private async Task TryAutoConnectToSavedDeviceAsync()
        {
            // If the service already thinks it's connected, just update the UI
            if (_metaWearService.IsConnected)
            {
                await UpdateIsConnectedStatusAsync(true);
                UpdateConnectionStatusIcon();
                await UpdateRecordButtonStateAsync();
                await DisplayAlert("Connected", "Already connected to your SmartDot device.", "OK");
                return;
            }

            // Check if user is logged in
            int userId = Preferences.Get("UserId", -1);
            if (userId == -1)
            {
                await DisplayAlert("Not Logged In", "Please log in to use auto-connect feature.", "OK");
                return;
            }

            try
            {
                // Get saved SmartDot MAC address
                string? savedMac = await _userRepository.GetSmartDotMacAsync(userId);

                if (string.IsNullOrEmpty(savedMac))
                {
                    await DisplayAlert("No Saved Device",
                        "No SmartDot device is saved for your account. Please connect to a device from the Bluetooth page first.",
                        "OK");
                    return;
                }

                // Show connecting state (orange)
                MmsConnectionToolbarItem.IconImageSource = new FontImageSource
                {
                    Glyph = "●",
                    FontFamily = "Arial",
                    Size = 20,
                    Color = Colors.Orange
                };

                // Check if Bluetooth is on
                if (!_ble.IsOn)
                {
                    await DisplayAlert("Bluetooth Off", "Please enable Bluetooth to connect to your SmartDot.", "OK");
                    UpdateConnectionStatusIcon();
                    return;
                }

                // Start scanning to find the device
                var devices = new System.Collections.Generic.List<IDevice>();
                bool deviceFound = false;
                IDevice? targetDevice = null;

                _adapter.DeviceDiscovered += (sender, e) =>
                {
                    if (e.Device != null &&
                        (e.Device.Name?.Contains("MetaWear", StringComparison.OrdinalIgnoreCase) == true ||
                         e.Device.Name?.Contains("MMS", StringComparison.OrdinalIgnoreCase) == true ||
                         e.Device.Name?.Contains("MMC", StringComparison.OrdinalIgnoreCase) == true ||
                         e.Device.Name?.Contains("MetaMotion", StringComparison.OrdinalIgnoreCase) == true))
                    {
                        string deviceId = e.Device.Id.ToString();
                        // Compare MAC addresses (handle different formats)
                        if (deviceId.Equals(savedMac, StringComparison.OrdinalIgnoreCase) ||
                            deviceId.Replace(":", "").Equals(savedMac.Replace(":", ""), StringComparison.OrdinalIgnoreCase) ||
                            deviceId.Replace("-", "").Equals(savedMac.Replace("-", ""), StringComparison.OrdinalIgnoreCase))
                        {
                            deviceFound = true;
                            targetDevice = e.Device;
                        }
                    }
                };

                // Scan for devices
                await _adapter.StartScanningForDevicesAsync();

                // Wait up to 5 seconds for device discovery
                for (int i = 0; i < 50 && !deviceFound; i++)
                {
                    await Task.Delay(100);
                }

                // Stop scanning
                await _adapter.StopScanningForDevicesAsync();

                if (targetDevice != null)
                {
                    // Connect to the device
                    bool connected = await _metaWearService.ConnectAsync(targetDevice);

                    if (connected)
                    {
                        // Update IsConnected status in database
                        await UpdateIsConnectedStatusAsync(true);

                        UpdateConnectionStatusIcon();
                        await UpdateRecordButtonStateAsync();
                        await DisplayAlert("Connected", $"Successfully connected to your SmartDot device.", "OK");
                    }
                    else
                    {
                        UpdateConnectionStatusIcon();
                        await DisplayAlert("Connection Failed",
                            "Found your device but failed to connect. Please try again or connect from the Bluetooth page.",
                            "OK");
                    }
                }
                else
                {
                    // Scan didn't find the device — check if it's already connected at the OS level
                    targetDevice = _adapter.ConnectedDevices?.FirstOrDefault(d =>
                    {
                        string deviceId = d.Id.ToString();
                        return deviceId.Equals(savedMac, StringComparison.OrdinalIgnoreCase) ||
                               deviceId.Replace(":", "").Equals(savedMac.Replace(":", ""), StringComparison.OrdinalIgnoreCase) ||
                               deviceId.Replace("-", "").Equals(savedMac.Replace("-", ""), StringComparison.OrdinalIgnoreCase);
                    });

                    if (targetDevice != null)
                    {
                        // Device is already connected at OS level — reconnect through the service
                        bool connected = await _metaWearService.ConnectAsync(targetDevice);

                        if (connected)
                        {
                            await UpdateIsConnectedStatusAsync(true);
                            UpdateConnectionStatusIcon();
                            await UpdateRecordButtonStateAsync();
                            await DisplayAlert("Connected", "Successfully connected to your SmartDot device.", "OK");
                        }
                        else
                        {
                            UpdateConnectionStatusIcon();
                            await DisplayAlert("Connection Failed",
                                "Found your device (already paired) but failed to connect. Please try again or connect from the Bluetooth page.",
                                "OK");
                        }
                    }
                    else
                    {
                        UpdateConnectionStatusIcon();
                        await DisplayAlert("Device Not Found",
                            "Could not find your saved SmartDot device. Please make sure:\n\n" +
                            "• The device is powered on\n" +
                            "• Bluetooth is enabled\n" +
                            "• The device is in range\n\n" +
                            "You can also connect manually from the Bluetooth page.",
                            "OK");
                    }
                }
            }
            catch (Exception ex)
            {
                UpdateConnectionStatusIcon();
                await DisplayAlert("Error", $"Failed to auto-connect: {ex.Message}", "OK");
            }
        }

        /// <summary>
        /// Updates the Record button enabled state based on SmartDot connection status
        /// </summary>
        private Task UpdateRecordButtonStateAsync()
        {
            try
            {
                bool connected = _metaWearService.IsConnected;

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    RecordBtn.IsEnabled = connected;
                    // Only update color if not currently recording
                    if (!isRecording)
                    {
                        RecordBtn.BackgroundColor = connected ? Color.FromArgb("#9880e5") : Colors.Gray;
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating record button state: {ex.Message}");
            }
            return Task.CompletedTask;
        }

        /// <summary>
        /// Updates the IsConnected status for the current user
        /// </summary>
        private async Task UpdateIsConnectedStatusAsync(bool isConnected)
        {
            try
            {
                int userId = Preferences.Get("UserId", -1);
                if (userId != -1)
                {
                    await _userRepository.UpdateIsConnectedAsync(userId, isConnected);
                    System.Diagnostics.Debug.WriteLine($"Updated IsConnected status: {isConnected} for user {userId}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating IsConnected status: {ex.Message}");
                // Don't show error to user - this is a background operation
            }
        }
    }
}
