using Cellular.Cloud_API.Models;
using CellularCore.Rendering;
using SkiaSharp;
using SkiaSharp.Views.Maui;

namespace Cellular.Views;

public partial class CiclopesPoseView : ContentView
{
    private readonly Camera3D _camera = new(azimuth: 0f, elevation: 0.15f);
    private readonly CiclopesSkeletonRenderer _renderer;
    private IReadOnlyList<List<CiclopesSkeletonPoint>> _allFrames = [];
    private double _lastPanX;
    private double _lastPanY;

    public CiclopesPoseView()
    {
        InitializeComponent();
        _renderer = new CiclopesSkeletonRenderer(_camera);
    }

    /// <summary>
    /// Load all skeleton frames. Call once after construction.
    /// </summary>
    public void LoadFrames(IReadOnlyList<List<CiclopesSkeletonPoint>> frames)
    {
        _allFrames = frames;
        if (frames.Count > 0)
            SetFrame(0);
    }

    /// <summary>
    /// Set the displayed frame index. Called by the popup's slider/playback.
    /// </summary>
    public void SetFrame(int frameIndex)
    {
        if (frameIndex < 0 || frameIndex >= _allFrames.Count) return;

        var frame = _allFrames[frameIndex];
        var joints = new Dictionary<int, (float X, float Y, float Z)>(frame.Count);
        foreach (var pt in frame)
        {
            // SAM3DBody outputs camera-space coords: X-right, Y-down, Z-forward.
            // Our renderer expects Y-up, so negate Y.
            joints[pt.JointId] = ((float)pt.X, -(float)pt.Y, (float)pt.Z);
        }

        _renderer.Joints = joints;
        SkeletonView.InvalidateSurface();
    }

    /// <summary>
    /// Toggle joint ID labels for debugging the skeleton mapping.
    /// </summary>
    public void ToggleLabels()
    {
        _renderer.ShowJointLabels = !_renderer.ShowJointLabels;
        SkeletonView.InvalidateSurface();
    }

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        var width = e.Info.Width;
        var height = e.Info.Height;

        _renderer.Draw(canvas, width, height);
    }

    private void OnPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _lastPanX = 0;
                _lastPanY = 0;
                break;

            case GestureStatus.Running:
                var deltaX = e.TotalX - _lastPanX;
                var deltaY = e.TotalY - _lastPanY;
                _lastPanX = e.TotalX;
                _lastPanY = e.TotalY;

                _camera.Rotate((float)deltaX, (float)deltaY);
                SkeletonView.InvalidateSurface();

                var azDeg = _camera.Azimuth * 180f / MathF.PI;
                var elDeg = _camera.Elevation * 180f / MathF.PI;
                CameraLabel.Text = $"{azDeg:F0}\u00B0 / {elDeg:F0}\u00B0";
                break;

            case GestureStatus.Completed:
            case GestureStatus.Canceled:
                CameraLabel.Text = "Drag to rotate";
                break;
        }
    }
}
