using Cellular.Cloud_API;
using Cellular.Cloud_API.Models;
using Cellular.Services;
using Cellular.Views;
using CommunityToolkit.Maui.Extensions;

namespace Cellular;

public partial class CiclopesExperimentalVideoPage : ContentPage
{
    private static readonly Color AccentColor = Color.FromArgb("#9880e5");

    private static readonly List<Size> CommonResolutions =
    [
        new Size(3840, 2160),
        new Size(2560, 1440),
        new Size(1920, 1080),
        new Size(1280, 720),
        new Size(854, 480)
    ];

    private static readonly string RevMetrixApiUsername = "string";
    private static readonly string RevMetrixApiPassword = "string";

    private bool _isCameraStarted;
    private bool _isRecording;
    private Size _previewResolution = new(1920, 1080);
    private string _currentVideoPath = string.Empty;
    private FileStream? _videoFileStream;

    public CiclopesExperimentalVideoPage()
    {
        InitializeComponent();

        cameraView.PropertyChanged += CameraView_PropertyChanged;
        cameraView.SizeChanged += CameraView_SizeChanged;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        await Permissions.RequestAsync<Permissions.Camera>();
        await Permissions.RequestAsync<Permissions.Microphone>();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();

        if (_isRecording)
        {
            _isRecording = false;
            _ = cameraView.StopVideoRecording(CancellationToken.None);
        }

        CleanupStream();
    }

    private void CameraView_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(cameraView.SelectedCamera))
        {
            return;
        }

        var selectedCamera = cameraView.SelectedCamera;
        if (selectedCamera == null)
        {
            return;
        }

        _isCameraStarted = true;
        StatusValueLabel.Text = "Camera ready. Tap record when you want to start.";
        RecordBtn.IsEnabled = true;

        if (selectedCamera.SupportedResolutions == null)
        {
            return;
        }

        var supportedResolutions = selectedCamera.SupportedResolutions
            .Where(available => CommonResolutions.Any(common =>
                (int)common.Width == (int)available.Width && (int)common.Height == (int)available.Height))
            .OrderByDescending(s => s.Width * s.Height)
            .ToList();

        ResolutionPicker.ItemDisplayBinding = new Binding(".", stringFormat: "{0.Width}x{0.Height}");
        ResolutionPicker.ItemsSource = supportedResolutions.Any()
            ? supportedResolutions
            : selectedCamera.SupportedResolutions.ToList();

        var defaultResolution = supportedResolutions.FirstOrDefault(s => Math.Abs(s.Width - 1920) < 10);
        if (defaultResolution.Width > 0)
        {
            ResolutionPicker.SelectedItem = defaultResolution;
        }
        else if (supportedResolutions.Count > 0)
        {
            ResolutionPicker.SelectedIndex = 0;
        }
    }

    private void CameraView_SizeChanged(object? sender, EventArgs e)
    {
        if (_isCameraStarted || cameraView.Width <= 0 || cameraView.Height <= 0)
        {
            return;
        }

        _isCameraStarted = true;
        RecordBtn.IsEnabled = true;
        StatusValueLabel.Text = "Camera ready. Tap record when you want to start.";

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await Task.Delay(100);
            try { cameraView.ImageCaptureResolution = _previewResolution; } catch { }
        });
    }

    private void ResolutionPicker_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (ResolutionPicker.SelectedItem is not Size selectedSize)
        {
            return;
        }

        _previewResolution = selectedSize;

        try { cameraView.ImageCaptureResolution = selectedSize; } catch { }
    }

    private async void OnRecordClicked(object sender, EventArgs e)
    {
        if (!_isRecording)
        {
            await StartRecordingAsync();
            return;
        }

        await StopRecordingAndProcessAsync();
    }

    private async Task StartRecordingAsync()
    {
        try
        {
            _isRecording = true;
            RecordBtn.Text = "Stop";
            RecordBtn.BackgroundColor = Colors.Red;
            StatusValueLabel.Text = "Recording in progress...";

            var targetFolder = Path.Combine(FileSystem.AppDataDirectory, "MyVideos");
            Directory.CreateDirectory(targetFolder);

            var fileName = $"rm_pose_demo_{DateTime.Now:yyyyMMdd_HHmmss}.mp4";
            _currentVideoPath = Path.Combine(targetFolder, fileName);

#if ANDROID
            AndroidCameraReset.RebuildVideoCapture(cameraView);
            await Task.Delay(600);
#endif

            _videoFileStream = File.Create(_currentVideoPath);
            await cameraView.StartVideoRecording(_videoFileStream, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _isRecording = false;
            RecordBtn.Text = "Record";
            RecordBtn.BackgroundColor = AccentColor;
            StatusValueLabel.Text = "Recording could not start.";
            CleanupStream();
            await DisplayAlert("Recording Error", ex.Message, "OK");
        }
    }

    private async Task StopRecordingAndProcessAsync()
    {
        if (!_isRecording || string.IsNullOrWhiteSpace(_currentVideoPath))
        {
            return;
        }

        RecordBtn.IsEnabled = false;
        SetBusyState(true, "Uploading video...");

        try
        {
            await cameraView.StopVideoRecording(CancellationToken.None);

#if ANDROID
            AndroidCameraReset.RebuildVideoCapture(cameraView);
#endif

            CleanupStream();

            _isRecording = false;
            RecordBtn.Text = "Record";
            RecordBtn.BackgroundColor = AccentColor;

            var videoPath = _currentVideoPath;
            _currentVideoPath = string.Empty;

            var videoKey = await UploadVideoAsync(videoPath);
            StatusValueLabel.Text = $"Video uploaded. Key: {videoKey}";

            var controller = new ApiController();
            var request = new CiclopesRunRequest
            {
                VideoKey = videoKey,
                SdKey = "key"
            };

            var poseTask = controller.ExecuteFourDBodyRunRequest(request);

            SetBusyState(false);
            await this.ShowPopupAsync(new CiclopesResultPopup(poseTask), CiclopesResultPopup.CreatePopupOptions());
        }
        catch (Exception ex)
        {
            StatusValueLabel.Text = "Upload or pose request failed.";
            await DisplayAlert("Pose Demo Failed", ex.Message, "OK");
        }
        finally
        {
            SetBusyState(false);
            RecordBtn.IsEnabled = true;
        }
    }

    private async Task<string> UploadVideoAsync(string videoPath)
    {
        if (!File.Exists(videoPath) || new FileInfo(videoPath).Length == 0)
        {
            try { if (File.Exists(videoPath)) File.Delete(videoPath); } catch { }
            throw new InvalidOperationException("The recording was empty. Please try again.");
        }

        var uploadService = new RevMetrixUploadService();
        try
        {
            var token = await uploadService.GetTokenAsync(RevMetrixApiUsername, RevMetrixApiPassword);
            if (string.IsNullOrWhiteSpace(token))
            {
                throw new InvalidOperationException("Could not get the upload API token.");
            }

            var videoKey = await uploadService.UploadFileAsync(token, videoPath, "videos", "video/mp4");
            try { File.Delete(videoPath); } catch { }
            return videoKey;
        }
        catch
        {
            try { if (File.Exists(videoPath)) File.Delete(videoPath); } catch { }
            throw;
        }
    }

    private void CleanupStream()
    {
        if (_videoFileStream == null)
        {
            return;
        }

        _videoFileStream.Flush();
        _videoFileStream.Dispose();
        _videoFileStream = null;
    }

    private void SetBusyState(bool isBusy, string? message = null)
    {
        BusyOverlay.IsVisible = isBusy;
        BusyLabel.Text = string.IsNullOrWhiteSpace(message) ? "Working..." : message;
        ResolutionPicker.IsEnabled = !isBusy && !_isRecording;
        cameraView.InputTransparent = isBusy;
    }
}
