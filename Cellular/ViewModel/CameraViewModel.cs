
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Cellular.ViewModel
{
    public partial class CameraViewModel : ObservableObject
    {
        public CameraViewModel()
        {
            Resolutions = new List<Size>
            {
                new Size(3840, 2160), // 4K UHD
                new Size(2560, 1440), // 1440p / QHD
                new Size(1920, 1080), // 1080p / Full HD
                new Size(1280, 720),  // 720p / HD
                new Size(854, 480)    // 480p / FWVGA
            };

            SelectedResolution = Resolutions[2]; // default to 1080p if available in list
        }

        public List<Size> Resolutions { get; }

        [ObservableProperty]
        object? selectedCamera;

        [ObservableProperty]
        Size selectedResolution;

        [ObservableProperty]
        string resolutionText = string.Empty;

        [ObservableProperty]
        string cameraNameText = string.Empty;

        partial void OnSelectedResolutionChanged(Size value)
        {
            ResolutionText = $"Selected Resolution: {value.Width} x {value.Height}";
        }

        partial void OnSelectedCameraChanged(object? oldValue, object? newValue)
        {
            if (newValue is null)
            {
                CameraNameText = string.Empty;
                return;
            }

            // Try to get a Name property if available, otherwise fall back to ToString()
            var nameProp = newValue.GetType().GetProperty("Name");
            if (nameProp != null)
            {
                var val = nameProp.GetValue(newValue);
                CameraNameText = val?.ToString() ?? newValue.ToString() ?? string.Empty;
            }
            else
            {
                CameraNameText = newValue.ToString() ?? string.Empty;
            }
        }
    }
}
