using Cellular.Cloud_API.Models;
using CommunityToolkit.Maui.Views;

namespace Cellular.Views;

public partial class CiclopesResultPopup : Popup
{
    private readonly CiclopesBallPointsDrawable _ballDrawable;
    private int _currentPaneIndex;
    private int _currentPlotIndex;
    private readonly int _maxPoseFrameIndex;
    private bool _isPlaying;
    private IDispatcherTimer? _playbackTimer;

    // The pane containers for animation
    private Grid[] _mainPanes = [];
    private Grid[] _plotPanels = [];
    private Border[] _plotDots = [];

    public CiclopesResultPopup(CiclopesRunResponse response)
    {
        InitializeComponent();

        _mainPanes = [BallPane, PosePane];
        _plotPanels = [PlotSpeedPanel, PlotAccelPanel, PlotLateralPanel];
        _plotDots = [PlotDot0, PlotDot1, PlotDot2];

        _ballDrawable = new CiclopesBallPointsDrawable(response.BallPoints);
        BallPlotView.Drawable = _ballDrawable;
        BallPlotView.Invalidate();

        _maxPoseFrameIndex = Math.Max(0, response.SkeletonPoints.Count - 1);
        FrameSlider.Maximum = _maxPoseFrameIndex;
        FrameSlider.Value = 0;

        PopulateStats(response);
        PopulatePlots(response);
        SetPane(0, false);
        SetPlotPanel(0, false);
    }

    private void PopulateStats(CiclopesRunResponse response)
    {
        var pts = response.BallPoints;
        var kin = response.KinematicsTable;

        if (pts.Count > 0)
        {
            var first = pts[0];
            var last = pts[^1];
            StatEntryX.Text = $"{first.X:F2} m";
            StatExitX.Text = $"{last.X:F2} m";
            StatCoverage.Text = $"{Math.Abs(last.Y - first.Y):F2} m";
            StatDrift.Text = $"{last.X - first.X:+0.00;-0.00;0.00} m";
        }

        if (kin.Count > 0)
        {
            var avgSpeed = kin.Average(k => k.MeanSpeedMps);
            var avgAccel = kin.Average(k => k.MeanAccelerationMps2);
            var entrySpeed = kin[0].MeanSpeedMps;
            var exitSpeed = kin[^1].MeanSpeedMps;

            StatAvgSpeed.Text = $"{avgSpeed:F2} m/s";
            StatAvgAccel.Text = $"{avgAccel:F2} m/s\u00B2";
            StatEntrySpeed.Text = $"{entrySpeed:F2} m/s";
            StatExitSpeed.Text = $"{exitSpeed:F2} m/s";
        }
    }

    private void PopulatePlots(CiclopesRunResponse response)
    {
        var kin = response.KinematicsTable;

        if (kin.Count > 0)
        {
            var speedValues = kin.Select(k => (float)k.MeanSpeedMps).ToArray();
            var speedLabels = kin.Select(k => $"Q{k.Quarter}").ToArray();
            SpeedPlotView.Drawable = new CiclopesBarPlotDrawable(speedValues, speedLabels,
                Color.FromArgb("#7c6bc4"), Color.FromArgb("#a594e0"));

            var accelValues = kin.Select(k => (float)k.MeanAccelerationMps2).ToArray();
            var accelLabels = kin.Select(k => $"Q{k.Quarter}").ToArray();
            AccelPlotView.Drawable = new CiclopesBarPlotDrawable(accelValues, accelLabels,
                Color.FromArgb("#4a6fa5"), Color.FromArgb("#7a9fd4"));
        }

        var pts = response.BallPoints;
        if (pts.Count > 1)
        {
            LateralPlotView.Drawable = new CiclopesLinePlotDrawable(pts);
        }
    }

    // ── Main pane switching (Ball / Pose) with animation ──

    private async void SetPane(int index, bool animate = true)
    {
        var oldIndex = _currentPaneIndex;
        _currentPaneIndex = Math.Clamp(index, 0, 1);

        if (animate && oldIndex != _currentPaneIndex)
        {
            var incoming = _mainPanes[_currentPaneIndex];
            var outgoing = _mainPanes[oldIndex];
            var slideDir = _currentPaneIndex > oldIndex ? 1 : -1;

            // Position incoming off-screen
            incoming.TranslationX = slideDir * 460;
            incoming.Opacity = 0;
            incoming.IsVisible = true;

            // Animate both
            await Task.WhenAll(
                outgoing.TranslateTo(-slideDir * 460, 0, 250, Easing.CubicInOut),
                outgoing.FadeTo(0, 200, Easing.CubicIn),
                incoming.TranslateTo(0, 0, 250, Easing.CubicInOut),
                incoming.FadeTo(1, 200, Easing.CubicOut)
            );

            outgoing.IsVisible = false;
            outgoing.TranslationX = 0;
            outgoing.Opacity = 1;
        }
        else
        {
            for (var i = 0; i < _mainPanes.Length; i++)
            {
                _mainPanes[i].IsVisible = i == _currentPaneIndex;
                _mainPanes[i].TranslationX = 0;
                _mainPanes[i].Opacity = 1;
            }
        }

        BallDot.Opacity = _currentPaneIndex == 0 ? 1.0 : 0.35;
        PoseDot.Opacity = _currentPaneIndex == 1 ? 1.0 : 0.35;
    }

    private void OnBallDotClicked(object? sender, TappedEventArgs e) => SetPane(0);
    private void OnPoseDotClicked(object? sender, TappedEventArgs e) => SetPane(1);

    private void OnMainSwipedLeft(object? sender, SwipedEventArgs e)
    {
        if (_currentPaneIndex < 1) SetPane(_currentPaneIndex + 1);
    }

    private void OnMainSwipedRight(object? sender, SwipedEventArgs e)
    {
        if (_currentPaneIndex > 0) SetPane(_currentPaneIndex - 1);
    }

    // ── Plot panel switching with animation ──

    private async void SetPlotPanel(int index, bool animate = true)
    {
        var oldIndex = _currentPlotIndex;
        _currentPlotIndex = Math.Clamp(index, 0, 2);

        if (animate && oldIndex != _currentPlotIndex)
        {
            var incoming = _plotPanels[_currentPlotIndex];
            var outgoing = _plotPanels[oldIndex];
            var slideDir = _currentPlotIndex > oldIndex ? 1 : -1;

            incoming.TranslationX = slideDir * 300;
            incoming.Opacity = 0;
            incoming.IsVisible = true;

            await Task.WhenAll(
                outgoing.TranslateTo(-slideDir * 300, 0, 200, Easing.CubicInOut),
                outgoing.FadeTo(0, 150, Easing.CubicIn),
                incoming.TranslateTo(0, 0, 200, Easing.CubicInOut),
                incoming.FadeTo(1, 150, Easing.CubicOut)
            );

            outgoing.IsVisible = false;
            outgoing.TranslationX = 0;
            outgoing.Opacity = 1;
        }
        else
        {
            for (var i = 0; i < _plotPanels.Length; i++)
            {
                _plotPanels[i].IsVisible = i == _currentPlotIndex;
                _plotPanels[i].TranslationX = 0;
                _plotPanels[i].Opacity = 1;
            }
        }

        for (var i = 0; i < _plotDots.Length; i++)
            _plotDots[i].Opacity = i == _currentPlotIndex ? 1.0 : 0.35;
    }

    private void OnPlotDot0Clicked(object? sender, TappedEventArgs e) => SetPlotPanel(0);
    private void OnPlotDot1Clicked(object? sender, TappedEventArgs e) => SetPlotPanel(1);
    private void OnPlotDot2Clicked(object? sender, TappedEventArgs e) => SetPlotPanel(2);

    private void OnPlotSwipedLeft(object? sender, SwipedEventArgs e)
    {
        if (_currentPlotIndex < 2) SetPlotPanel(_currentPlotIndex + 1);
    }

    private void OnPlotSwipedRight(object? sender, SwipedEventArgs e)
    {
        if (_currentPlotIndex > 0) SetPlotPanel(_currentPlotIndex - 1);
    }

    // ── Close button ──

    private async void OnCloseClicked(object? sender, TappedEventArgs e)
    {
        await CloseAsync();
    }

    // ── Pose playback ──

    private void OnFrameSliderValueChanged(object? sender, ValueChangedEventArgs e)
    {
        _ = (int)Math.Round(e.NewValue);
    }

    private void OnPlayStopClicked(object? sender, EventArgs e)
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
            if (!_isPlaying) return;

            var next = (int)Math.Round(FrameSlider.Value) + 1;
            if (next > _maxPoseFrameIndex) next = 0;
            FrameSlider.Value = next;
        };
        return timer;
    }
}
