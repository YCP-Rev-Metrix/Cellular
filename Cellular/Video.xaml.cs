using Camera.MAUI;
using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Storage;
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
    public partial class Video : ContentPage
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
        private string currentVideoPath;

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
            
            // Check actual connection status and update icon immediately
            // The service is a singleton, so it maintains connection state
            UpdateConnectionStatusIcon();
        }
        
        public Video()
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

            cameraView.CamerasLoaded += CameraView_CamerasLoaded;
            cameraView.SizeChanged += CameraView_SizeChanged;
            
            // Don't update icon here - wait for OnAppearing when service is definitely ready
        }

        private void CameraView_CamerasLoaded(object sender, EventArgs e)
        {
            if (cameraView.Cameras.Count > 0)
            {
                // Select camera
                cameraView.Camera = cameraView.Cameras.FirstOrDefault(c => c.Position == Camera.MAUI.CameraPosition.Back);

                // Populate the ResolutionPicker
                if (cameraView.Camera != null)
                {
                    var supportedResolutions = cameraView.Camera.AvailableResolutions
                        .Where(available => CommonResolutions.Any(common =>
                            common.Width == available.Width && common.Height == available.Height))
                        .OrderByDescending(s => s.Width * s.Height)
                        .ToList();

                    // Set the display format for the Picker items
                    ResolutionPicker.ItemDisplayBinding = new Binding(".", stringFormat: "{0.Width}x{0.Height}");

                    ResolutionPicker.ItemsSource = supportedResolutions;

                    // Try to find and select the default resolution (1080p)
                    var defaultResolution = supportedResolutions
                        .FirstOrDefault(s => s.Width == 1920 && s.Height == 1080);

                    if (defaultResolution != null)
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
        private async void ResolutionPicker_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (ResolutionPicker.SelectedItem is Size selectedSize)
            {
                // Update the internal resolution field
                previewResolution = selectedSize;

                // If the camera is already running, stop and restart it with the new resolution
                if (isCameraStarted)
                {
                    await cameraView.StopCameraAsync();
                    isCameraStarted = false;

                    CameraView_SizeChanged(cameraView, EventArgs.Empty);
                }
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

                    // Start sensor data buffering with video filename
                    if (_sensorBufferManager != null)
                    {
                        string baseFileName = Path.GetFileNameWithoutExtension(fileName);
                        await _sensorBufferManager.StartBufferingAsync(baseFileName);
                    }

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
                    
                    // Stop buffering if it was started
                    if (_sensorBufferManager != null)
                    {
                        await _sensorBufferManager.StopBufferingAsync();
                    }
                }
            }
            else
            {
                try
                {
                    // Stop sensor buffering
                    if (_sensorBufferManager != null)
                    {
                        await _sensorBufferManager.StopBufferingAsync();
                    }

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

            cameraView.StopCameraAsync();
        }

        private void OnSensorDataSaved(object? sender, SensorDataSavedEventArgs e)
        {
            // This event is no longer used - continuous save starts directly when light sensor hits threshold
            // Keeping this handler to avoid breaking the event subscription, but it won't be called
            System.Diagnostics.Debug.WriteLine($"[Video] OnSensorDataSaved called but ignored (continuous save starts directly now)");
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
            // Show file picker to let user choose save location
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                // Small delay to ensure any other popups are dismissed
                await Task.Delay(500);
                
                // Verify temp file exists
                if (!File.Exists(e.FilePath))
                {
                    await DisplayAlert("Error", 
                        $"Sensor data temp file not found at:\n{e.FilePath}\n\n" +
                        $"Please check the debug logs for details.", 
                        "OK");
                    return;
                }

                try
                {
                    // Open the temp file as a stream
                    using var sensorDataStream = File.OpenRead(e.FilePath);
                    
                    // Get the filename
                    string fileName = Path.GetFileName(e.FilePath);
                    
                    // Use the last save path if available, otherwise use empty string
                    string initialPath = App.LastSavePath;
                    
                    // Show file picker to let user choose where to save
                    var saveResult = await FileSaver.Default.SaveAsync(initialPath, fileName, sensorDataStream, CancellationToken.None);
                    
                    if (saveResult.IsSuccessful)
                    {
                        // Update last save path for next time
                        App.LastSavePath = Path.GetDirectoryName(saveResult.FilePath);
                        
                        // Delete the temp file after successful save
                        try
                        {
                            File.Delete(e.FilePath);
                        }
                        catch (Exception deleteEx)
                        {
                            System.Diagnostics.Debug.WriteLine($"[Video] Error deleting temp file: {deleteEx.Message}");
                        }
                        
                        await DisplayAlert("Success", 
                            $"Sensor data saved successfully!\n\n" +
                            $"File: {Path.GetFileName(saveResult.FilePath)}\n" +
                            $"Location: {Path.GetDirectoryName(saveResult.FilePath)}\n" +
                            $"Data Points: {e.DataPointCount}\n\n" +
                            $"4 seconds of data have been saved.", 
                            "OK");
                    }
                    else
                    {
                        await DisplayAlert("Error", 
                            $"Failed to save sensor data: {saveResult.Exception?.Message ?? "Unknown error"}", 
                            "OK");
                    }
                }
                catch (Exception ex)
                {
                    await DisplayAlert("Error", 
                        $"Error preparing to save sensor data: {ex.Message}", 
                        "OK");
                }
            });
        }

        private void OnDeviceDisconnected(object? sender, string macAddress)
        {
            // Update icon when device disconnects
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                // Update IsConnected status in database
                await UpdateIsConnectedStatusAsync(false);
                
                UpdateConnectionStatusIcon();
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
                
                // Update icon
                UpdateConnectionStatusIcon();
                
                await DisplayAlert("Disconnected", "Successfully disconnected from MMS device.", "OK");
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Failed to disconnect: {ex.Message}", "OK");
            }
        }

        private async Task TryAutoConnectToSavedDeviceAsync()
        {
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
                         e.Device.Name?.Contains("MMS", StringComparison.OrdinalIgnoreCase) == true))
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
            catch (Exception ex)
            {
                UpdateConnectionStatusIcon();
                await DisplayAlert("Error", $"Failed to auto-connect: {ex.Message}", "OK");
            }
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