using Cellular.Cloud_API.Models;
using CommunityToolkit.Maui.Views;

namespace Cellular.Views;

public partial class CiclopesResultPopup : Popup
{
    private readonly CiclopesBallPointsDrawable _ballDrawable;
    private int _currentPaneIndex;
    private readonly int _maxPoseFrameIndex;
    private bool _isPlaying;
    private IDispatcherTimer? _playbackTimer;

    public CiclopesResultPopup(CiclopesRunResponse response)
    {
        InitializeComponent();

        _ballDrawable = new CiclopesBallPointsDrawable(response.BallPoints);
        BallPlotView.Drawable = _ballDrawable;
        BallPlotView.Invalidate();

        _maxPoseFrameIndex = Math.Max(0, response.SkeletonPoints.Count - 1);
        FrameSlider.Maximum = _maxPoseFrameIndex;
        FrameSlider.Value = 0;

        PopulateStats(response);
        SetPane(0);
    }

    private void PopulateStats(CiclopesRunResponse response)
    {
        var pts = response.BallPoints;
        var kin = response.KinematicsTable;

        // Position stats
        if (pts.Count > 0)
        {
            var first = pts[0];
            var last = pts[^1];
            StatEntryX.Text = $"{first.X:F2} m";
            StatExitX.Text = $"{last.X:F2} m";
            StatDrift.Text = $"{last.X - first.X:+0.00;-0.00;0.00} m";
            StatCoverage.Text = $"{Math.Abs(last.Y - first.Y):F2} m";
            StatSamples.Text = $"{pts.Count}";
        }

        if (kin.Count > 0)
        {
            StatQuarters.Text = $"{kin.Count}";

            var avgSpeed = kin.Average(k => k.MeanSpeedMps);
            var peakSpeed = kin.Max(k => k.MeanSpeedMps);
            var avgAccel = kin.Average(k => k.MeanAccelerationMps2);
            var peakAccel = kin.Max(k => Math.Abs(k.MeanAccelerationMps2));
            var entrySpeed = kin[0].MeanSpeedMps;
            var exitSpeed = kin[^1].MeanSpeedMps;

            StatAvgSpeed.Text = $"{avgSpeed:F2} m/s";
            StatPeakSpeed.Text = $"{peakSpeed:F2} m/s";
            StatAvgAccel.Text = $"{avgAccel:F2} m/s\u00B2";
            StatPeakAccel.Text = $"{peakAccel:F2} m/s\u00B2";
            StatEntrySpeed.Text = $"{entrySpeed:F2} m/s";
            StatExitSpeed.Text = $"{exitSpeed:F2} m/s";
        }
        else if (pts.Count > 0)
        {
            StatQuarters.Text = "0";
        }
    }

    private void SetPane(int index)
    {
        _currentPaneIndex = Math.Clamp(index, 0, 1);

        BallPane.IsVisible = _currentPaneIndex == 0;
        PosePane.IsVisible = _currentPaneIndex == 1;

        BallDot.Opacity = _currentPaneIndex == 0 ? 1.0 : 0.35;
        PoseDot.Opacity = _currentPaneIndex == 1 ? 1.0 : 0.35;
    }

    private void OnBallDotClicked(object sender, TappedEventArgs e)
    {
        SetPane(0);
    }

    private void OnPoseDotClicked(object sender, TappedEventArgs e)
    {
        SetPane(1);
    }

    private void OnSwipedLeft(object sender, SwipedEventArgs e)
    {
        if (_currentPaneIndex < 1)
        {
            SetPane(_currentPaneIndex + 1);
        }
    }

    private void OnSwipedRight(object sender, SwipedEventArgs e)
    {
        if (_currentPaneIndex > 0)
        {
            SetPane(_currentPaneIndex - 1);
        }
    }

    private void OnFrameSliderValueChanged(object sender, ValueChangedEventArgs e)
    {
        _ = (int)Math.Round(e.NewValue);
    }

    private void OnPlayStopClicked(object sender, EventArgs e)
    {
        if (_isPlaying)
        {
            _isPlaying = false;
            _playbackTimer?.Stop();
            PlayStopButton.Text = "\u25B6";
        }
        else
        {
            if (_maxPoseFrameIndex <= 0) return;
            _isPlaying = true;
            _playbackTimer ??= CreatePlaybackTimer();
            _playbackTimer.Start();
            PlayStopButton.Text = "\u25A0";
        }
    }

    private IDispatcherTimer CreatePlaybackTimer()
    {
        var timer = Dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(120);
        timer.Tick += (_, _) =>
        {
            if (!_isPlaying)
            {
                return;
            }

            var next = (int)Math.Round(FrameSlider.Value) + 1;
            if (next > _maxPoseFrameIndex)
            {
                next = 0;
            }

            FrameSlider.Value = next;
        };

        return timer;
    }
}
