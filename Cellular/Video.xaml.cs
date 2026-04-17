using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Storage;
using CommunityToolkit.Maui.Views;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using System;
using System.IO;
using Cellular.Services;
using Cellular.Data;
using Cellular.Cloud_API;
using Cellular.Cloud_API.Models;
using Plugin.BLE;
using Plugin.BLE.Abstractions.Contracts;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Maui.Extensions;
using Cellular.Cloud_API;
using Cellular.Cloud_API.Models;

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
        private string currentVideoPath = string.Empty;
        private System.IO.FileStream? _videoFileStream;

        // Stop video only after log is saved; timeout if no log save happens
        private CancellationTokenSource? _stopVideoCts;


        // Upload to RevMetrix API (Digital Ocean Space) - use your API credentials
        private static readonly string RevMetrixApiUsername = "string";
        private static readonly string RevMetrixApiPassword = "string";

        // Keys from last successful upload — used to send to Ciclopes
        private string? _lastVideoKey;
        private string? _lastSdKey;

        // Sensor data buffering
        private SensorBufferManager? _sensorBufferManager;
        private IMetaWearService _metaWearService; // Not readonly so we can update to singleton if needed
        private readonly UserRepository _userRepository;
        private readonly IBluetoothLE _ble = CrossBluetoothLE.Current;
        private readonly IAdapter _adapter = CrossBluetoothLE.Current.Adapter;
        private IWatchBleService _watchBleService;

        // Demo mode state
        private bool _isCameraMode = true;
        private bool isDemoPlaying = false;


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
                    _metaWearService.DeviceReconnected -= OnDeviceReconnected;
                    _metaWearService = serviceFromDI;
                }
            }

            // Re-subscribe to events in case page was recreated
            _metaWearService.DeviceDisconnected -= OnDeviceDisconnected; // Remove first to avoid duplicates
            _metaWearService.DeviceDisconnected += OnDeviceDisconnected;
            _metaWearService.DeviceReconnected -= OnDeviceReconnected;
            _metaWearService.DeviceReconnected += OnDeviceReconnected;

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
            _metaWearService.DeviceReconnected -= OnDeviceReconnected;
            _metaWearService.DeviceReconnected += OnDeviceReconnected;

            // Initialize sensor buffer manager
            _sensorBufferManager = new SensorBufferManager(_metaWearService);
            _sensorBufferManager.DataSaved += OnSensorDataSaved;
            _sensorBufferManager.SaveError += OnSensorSaveError;
            _sensorBufferManager.ContinuousSaveStarted += OnContinuousSaveStarted;
            _sensorBufferManager.ContinuousSaveComplete += OnContinuousSaveComplete;
            //Initialize watch services
            _watchBleService = Handler?.MauiContext?.Services?.GetService<IWatchBleService>()
                ?? Application.Current?.Handler?.MauiContext?.Services?.GetService<IWatchBleService>()
                ?? Microsoft.Maui.Controls.Application.Current?.Handler?.MauiContext?.Services?.GetService<IWatchBleService>()
                ?? new WatchBleService(); // Last resort - but this should not happen with proper DI

            // Subscribe to watch recording commands
            _watchBleService.WatchStartRecordingRequested += async (s, e) => await BeginExternalRecording();
            _watchBleService.WatchStopRecordingRequested += async (s, e) => await EndExternalRecording();


            cameraView.PropertyChanged += CameraView_PropertyChanged;
            cameraView.SizeChanged += CameraView_SizeChanged;

            // Setup media element for packed Raw asset and diagnostics
            try
            {
                // FromResource is the correct API for Resources/Raw MauiAsset files.
                // On Android it resolves via Assets/ internally (ExoPlayer).
                // FromFile is for filesystem paths and does NOT work for bundled raw assets.
                mediaElement.Source = MediaSource.FromResource("lego.mp4");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to set media source from resource: {ex.Message}");
                // Fallback: try a known remote URL to verify playback support on device
                // try
                // {
                //     mediaElement.Source = MediaSource.FromUri("https://commondatastorage.googleapis.com/gtv-videos-bucket/sample/BigBuckBunny.mp4");
                //     mediaElement.IsVisible = true;
                //     mediaElement.Play();
                // }
                // catch { }
            }

            mediaElement.MediaOpened += (s, e) =>
            {
                System.Diagnostics.Debug.WriteLine("Media opened");
                // In demo mode, start playback once Android ExoPlayer has prepared the source
                if (isDemoPlaying)
                {
                    mediaElement.Play();
                }
            };
            mediaElement.MediaFailed += (s, e) =>
            {
                System.Diagnostics.Debug.WriteLine($"Media failed: {e.ErrorMessage}");
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    try { await DisplayAlert("Media Error", e.ErrorMessage ?? "Unknown media error", "OK"); } catch { }
                });
            };
            mediaElement.MediaEnded += (s, e) =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (isDemoPlaying)
                    {
                        isDemoPlaying = false;
                        DemoRecordBtn.Text = "Play";
                        DemoRecordBtn.BackgroundColor = Color.FromArgb("#9880e5");

                        // Show Ciclopes button with demo/test keys
                        UseCiclopesBtn.IsVisible = true;
                        UseCiclopesBtn.IsEnabled = true;
                        UseCiclopesBtn.BackgroundColor = Color.FromArgb("#9880e5");
                        _lastVideoKey = "videos/310fceda-dac8-4bf0-a25c-2d1ba360ea68_shot1.mp4";
                        _lastSdKey = "key";
                    }
                });
            };
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
                await StartRecordingAsync();
            else
                await StopRecordingAsync();
        }

        /// <summary>
        /// Core start-recording logic shared by the phone button and the watch command.
        /// Must be called on the main thread.
        /// </summary>
        private async Task StartRecordingAsync()
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

                if (_sensorBufferManager != null)
                {
                    string baseFileName = Path.GetFileNameWithoutExtension(fileName);
                    await _sensorBufferManager.StartBufferingAsync(baseFileName);
                }

                // On Android, the toolkit reuses the same Recorder between recordings.
                // After the first stop, the Recorder's MediaCodec audio encoder is left in a
                // stale CONFIGURED state. Calling PrepareRecording on it fails with error code 6
                // (C2_NO_MEMORY) because the encoder slot is still partially occupied.
                // RebuildVideoCapture() disposes the stale Recorder and creates a fresh one
                // WITHOUT touching the Preview — so no black screen, no handler disconnect needed.
#if ANDROID
                // Two-stage rebuild to reliably release the native MediaCodec audio encoder slot:
                //   Stage 1 (in StopVideoAndUploadVideoOnlyAsync): disposes the Recorder from the
                //     previous recording immediately after stop, starting native cleanup early.
                //   Stage 2 (here): disposes whatever the upload-dialog's MainActivity pause may have
                //     corrupted, creates a clean Recorder B that has never seen a STOPPING state,
                //     then waits 600ms so the native encoder slot from stage 1 is fully free before
                //     PrepareRecording runs.
                AndroidCameraReset.RebuildVideoCapture(cameraView);
                await Task.Delay(600);
#endif
                _videoFileStream = File.Create(currentVideoPath);
                await cameraView.StartVideoRecording(_videoFileStream, CancellationToken.None);
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Failed to start recording: {ex.Message}", "OK");
                isRecording = false;
                RecordBtn.Text = "Record";
                RecordBtn.BackgroundColor = Color.FromArgb("#9880e5");
                if (_sensorBufferManager != null)
                    await _sensorBufferManager.StopBufferingAsync();
                CleanupStream();
            }
        }

        /// <summary>
        /// Core stop-recording logic shared by the phone button and the watch command.
        /// Stops sensor buffering immediately; video stops after the log saves or after a 5-second timeout.
        /// Must be called on the main thread.
        /// </summary>
        private async Task StopRecordingAsync()
        {
            try
            {
                _stopVideoCts?.Cancel();
                _stopVideoCts = new CancellationTokenSource();
                var cts = _stopVideoCts;

                if (_sensorBufferManager != null)
                    await _sensorBufferManager.StopBufferingAsync();

                // If OnContinuousSaveComplete fires, it will cancel this and stop the video after
                // saving the log. Otherwise fall back to stopping after 5 seconds with video only.
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
        /// <summary>
        /// Stops the video and uploads only the video (no log). Used when log save never completed (timeout).
        /// </summary>
        private async Task StopVideoAndUploadVideoOnlyAsync()
        {
            if (!isRecording || string.IsNullOrEmpty(currentVideoPath)) return;
            RecordBtn.IsEnabled = false;
            try
            {
                // Stop camera BEFORE resetting isRecording so the user cannot start a new
                // recording while StopVideoRecording is still in progress.
                await cameraView.StopVideoRecording(CancellationToken.None);

#if ANDROID
                // Stage 1 of the two-stage native encoder reset: dispose the stale Recorder
                // immediately after the recording stops so the native c2.android.aac.encoder
                // slot starts releasing as early as possible. By the time the user dismisses the
                // upload dialog and presses Record again, the hardware should be free for Stage 2.
                AndroidCameraReset.RebuildVideoCapture(cameraView);
#endif

                CleanupStream();
                isRecording = false;
                RecordBtn.Text = "Record";
                RecordBtn.BackgroundColor = Color.FromArgb("#9880e5");
                if (File.Exists(currentVideoPath))
                    await SaveAndUploadVideoAsync(currentVideoPath, logBytesForUpload: null, logFileNameForUpload: null, folderToSaveVideoTo: null);
            }
            catch (Exception ex)
            {
                isRecording = false;
                RecordBtn.Text = "Record";
                RecordBtn.BackgroundColor = Color.FromArgb("#9880e5");
                await DisplayAlert("Error", $"Failed to stop/upload video: {ex.Message}", "OK");
            }
            finally
            {
                RecordBtn.IsEnabled = true;
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
            // Guard: if the video file is missing or empty the camera encoder never wrote any frames
            // (e.g. a stale stop-timer fired before the encoder was ready). Skip silently.
            if (!File.Exists(videoPath) || new FileInfo(videoPath).Length == 0)
            {
                try { if (File.Exists(videoPath)) File.Delete(videoPath); } catch { }
                await DisplayAlert("Recording Error", "The recording was empty — the camera encoder had not started writing when stop was triggered. Please try recording again.", "OK");
                return;
            }

            // Save video to the phone using FileSaver (same folder as log when provided). On Android, folder path may be denied — retry with no initial path.
            string? savedVideoPath = null;
            string? saveFolder = folderToSaveVideoTo ?? App.LastSavePath;
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
                _lastVideoKey = videoKey;
                _lastSdKey = logKey;
                UpdateCiclopesButtonState();
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

            // Cancel any pending "stop after 5s" timeout so it cannot fire against
            // the next recording when the user returns to this page.
            _stopVideoCts?.Cancel();
            _stopVideoCts = null;

            // If the user navigated away while a recording was active, reset the state
            // so the page is clean when they return.
            if (isRecording)
            {
                isRecording = false;
                RecordBtn.Text = "Record";
                RecordBtn.BackgroundColor = Color.FromArgb("#9880e5");
                RecordBtn.IsEnabled = true;
                _ = cameraView.StopVideoRecording(CancellationToken.None);
            }

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

            isCameraStarted = false;

            // Unsubscribe from watch recording events
            _watchBleService.WatchStartRecordingRequested -= async (s, e) => await BeginExternalRecording();
            _watchBleService.WatchStopRecordingRequested -= async (s, e) => await EndExternalRecording();

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

                    // Stop the video recording now that the log is saved (or user cancelled).
                    // Keep RecordBtn disabled until the camera fully stops to prevent
                    // StartVideoRecording being called while StopVideoRecording is in progress.
                    if (isRecording && !string.IsNullOrEmpty(currentVideoPath))
                    {
                        RecordBtn.IsEnabled = false;
                        try
                        {
                            await cameraView.StopVideoRecording(CancellationToken.None);
                            CleanupStream();
                            isRecording = false;
                            RecordBtn.Text = "Record";
                            RecordBtn.BackgroundColor = Color.FromArgb("#9880e5");
                            if (File.Exists(currentVideoPath))
                                await SaveAndUploadVideoAsync(currentVideoPath, logBytesForUpload, logFileNameForUpload, folderToSaveVideoTo: logSavedDirectory);
                        }
                        finally
                        {
                            isRecording = false;
                            RecordBtn.Text = "Record";
                            RecordBtn.BackgroundColor = Color.FromArgb("#9880e5");
                            RecordBtn.IsEnabled = true;
                        }
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

        

        private void UpdateCiclopesButtonState()
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                bool hasKeys = !string.IsNullOrEmpty(_lastVideoKey);
                UseCiclopesBtn.IsEnabled = hasKeys;
                UseCiclopesBtn.BackgroundColor = hasKeys ? Color.FromArgb("#9880e5") : Colors.Gray;
            });
        }

        private async void OnUseCiclopesClicked(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_lastVideoKey))
            {
                await DisplayAlert("Ciclopes", "No uploaded video found. Record and upload a video first.", "OK");
                return;
            }

            UseCiclopesBtn.IsEnabled = false;
            try
            {
                var controller = new ApiController();
                var request = new CiclopesRunRequest
                {
                    VideoKey = _lastVideoKey,
                    SdKey = _lastSdKey ?? string.Empty
                };

                var laneBallsTask = controller.ExecuteLaneBallsRunRequest(request);
                var fourDBodyTask = controller.ExecuteFourDBodyRunRequest(request);

                var laneBallsResponse = await laneBallsTask;

                if (laneBallsResponse is null)
                {
                    await DisplayAlert("Ciclopes", "No lane/balls data returned.", "OK");
                    return;
                }

                this.ShowPopup(new Cellular.Views.CiclopesResultPopup(laneBallsResponse, fourDBodyTask));
            }
            catch (Exception ex)
            {
                await DisplayAlert("Ciclopes Request Failed", ex.Message, "OK");
            }
            finally
            {
                UpdateCiclopesButtonState();
            }
        }

        private void OnDeviceDisconnected(object? sender, string macAddress)
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await UpdateIsConnectedStatusAsync(false);
                UpdateConnectionStatusIcon();
                await UpdateRecordButtonStateAsync();
            });
        }

        private void OnDeviceReconnected(object? sender, string macAddress)
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await UpdateIsConnectedStatusAsync(true);
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

        /// <summary>
        /// Starts video recording when triggered by the watch. Mirrors pressing Record on the phone.
        /// </summary>
        public async Task BeginExternalRecording()
        {
            if (!isCameraStarted || isRecording)
                return;

            await MainThread.InvokeOnMainThreadAsync(StartRecordingAsync);
        }

        /// <summary>
        /// Stops video recording when triggered by the watch. Mirrors pressing Stop on the phone.
        /// </summary>
        public async Task EndExternalRecording()
        {
            if (!isRecording)
                return;

            await MainThread.InvokeOnMainThreadAsync(StopRecordingAsync);
        }
    }
}
