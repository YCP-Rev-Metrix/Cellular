using Cellular.Cloud_API.Models;
using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Views;
using Microsoft.Maui.Controls.Shapes;
using System.Diagnostics;

namespace Cellular.Views;

public partial class CiclopesResultPopup : Popup
{
    // Unit conversion constants
    private const double MetersToInches = 39.3701;
    private const double MetersToFeet = 3.28084;
    private const double MpsToMph = 2.23694;
    private const double Mps2ToFtps2 = 3.28084;

    private CiclopesBallPointsDrawable _ballDrawable = null!;
    private int _currentPaneIndex;

    // Multi-overlay state
    private readonly bool _isMultiMode;
    private Dictionary<string, LaneBallsShotData> _laneShots = [];
    private Dictionary<string, FourDBodyShotData> _poseShots = [];
    private List<string> _shotOrder = [];
    private readonly Dictionary<string, Color> _shotColors = new();
    private readonly Dictionary<string, Border> _shotChips = new();
    private string? _activeShot;

    private static readonly Color[] ShotPalette =
    [
        Color.FromArgb("#355070"), // Primary
        Color.FromArgb("#FA8847"), // Secondary
        Color.FromArgb("#2E7D52"), // Success green
        Color.FromArgb("#C0392B"), // Error red
        Color.FromArgb("#8E44AD"), // Purple
        Color.FromArgb("#1E3448")  // Tertiary
    ];

    private int _currentPlotIndex;
    private int _maxPoseFrameIndex;
    private bool _isPlaying;
    private IDispatcherTimer? _playbackTimer;

    // Playback speed
    private float _poseFps = 30f;
    private static readonly double[] SpeedMultipliers = [1.0, 0.75, 0.5, 0.25];
    private static readonly string[] SpeedLabels = ["1x", "0.75x", "0.5x", "0.25x"];
    private double _speedMultiplier = 1.0;

    // The pane containers for animation
    private Grid[] _mainPanes = [];
    private Grid[] _plotPanels = [];
    private Border[] _plotDots = [];

    public CiclopesResultPopup(LaneBallsRunResponse laneBallsResponse, Task<FourDBodyRunResponse?> fourDBodyTask)
    {
        InitializeComponent();
        InitCommonChrome();
        _isMultiMode = false;

        _ballDrawable = new CiclopesBallPointsDrawable(laneBallsResponse.BallPoints);
        BallPlotView.Drawable = _ballDrawable;
        BallPlotView.Invalidate();

        _maxPoseFrameIndex = 0;
        FrameSlider.Maximum = 0;
        FrameSlider.Value = 0;

        PopulateStats(laneBallsResponse);
        PopulatePlots(laneBallsResponse);

        SetPane(0, false);
        SetPlotPanel(0, false);

        // Fire-and-forget: load pose data when it arrives
        _ = LoadPoseDataAsync(fourDBodyTask);
    }

    public CiclopesResultPopup(LaneBallsQueryResponse laneBallsQuery, Task<FourDBodyQueryResponse?> fourDBodyQueryTask)
    {
        InitializeComponent();
        InitCommonChrome();
        _isMultiMode = true;

        _laneShots = laneBallsQuery.Shots ?? [];
        _shotOrder = _laneShots.Keys.OrderBy(k => int.TryParse(k, out var n) ? n : 0).ToList();

        for (var i = 0; i < _shotOrder.Count; i++)
            _shotColors[_shotOrder[i]] = ShotPalette[i % ShotPalette.Length];

        BuildShotChips();

        // Push ball pane content down so chips don't crowd the cards
        BallPane.Margin = new Thickness(0, 36, 0, 0);

        var series = _shotOrder
            .Select(k => (_shotColors[k], (IReadOnlyList<CiclopesBallPoint>)_laneShots[k].BallPoints))
            .ToList();
        _ballDrawable = new CiclopesBallPointsDrawable(series);
        BallPlotView.Drawable = _ballDrawable;
        BallPlotView.Invalidate();

        _maxPoseFrameIndex = 0;
        FrameSlider.Maximum = 0;
        FrameSlider.Value = 0;

        if (_shotOrder.Count > 0)
        {
            SetActiveShot(_shotOrder[0]);
        }

        SetPane(0, false);
        SetPlotPanel(0, false);

        _ = LoadQueryPoseDataAsync(fourDBodyQueryTask);
    }

    public CiclopesResultPopup(Task<FourDBodyRunResponse?> fourDBodyTask)
    {
        InitializeComponent();
        InitCommonChrome();
        _isMultiMode = false;

        ConfigurePoseOnlyMode();

        _maxPoseFrameIndex = 0;
        FrameSlider.Maximum = 0;
        FrameSlider.Value = 0;

        SetPane(1, false);

        _ = LoadPoseDataAsync(fourDBodyTask);
    }

    private void InitCommonChrome()
    {
        // Hardcoded for Samsung Galaxy S23 viewport (~360x780 DIPs). Tuned to
        // fit comfortably inside that screen with margin for status/nav bars.
        // Revisit if other form factors need to be supported.
        const double popupWidth = 340;
        const double popupHeight = 700;
        const double laneWidth = 160;

        RootContent.WidthRequest = popupWidth;
        RootContent.HeightRequest = popupHeight;

        if (BallPane.ColumnDefinitions.Count > 0)
            BallPane.ColumnDefinitions[0].Width = new GridLength(laneWidth);

        _mainPanes = [BallPane, PosePane];
        _plotPanels = [PlotSpeedPanel, PlotAccelPanel, PlotLateralPanel];
        _plotDots = [PlotDot0, PlotDot1, PlotDot2];

        foreach (var label in SpeedLabels)
            SpeedPicker.Items.Add(label);
        SpeedPicker.SelectedIndex = 0;
    }

    private void ConfigurePoseOnlyMode()
    {
        BallPane.IsVisible = false;
        BallDot.IsVisible = false;
        BallDot.InputTransparent = true;
        PoseDot.Opacity = 1.0;
        PoseDot.InputTransparent = true;
        PoseLoadingLabel.Text = "Running pose estimation...";
        ShotChipBar.IsVisible = false;
    }

    private void BuildShotChips()
    {
        ShotChipBar.IsVisible = _shotOrder.Count > 0;
        ShotChipBar.Children.Clear();
        _shotChips.Clear();

        foreach (var key in _shotOrder)
        {
            var color = _shotColors[key];
            var chip = new Border
            {
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(12) },
                StrokeThickness = 1.5,
                Stroke = color,
                BackgroundColor = Colors.Transparent,
                Padding = new Thickness(10, 4),
                Content = new Label
                {
                    Text = $"Shot {key}",
                    FontSize = 11,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = color,
                    VerticalTextAlignment = TextAlignment.Center
                }
            };

            var tap = new TapGestureRecognizer();
            var capturedKey = key;
            tap.Tapped += (_, _) => SetActiveShot(capturedKey);
            chip.GestureRecognizers.Add(tap);

            _shotChips[key] = chip;
            ShotChipBar.Children.Add(chip);
        }
    }

    private void SetActiveShot(string key)
    {
        if (!_laneShots.TryGetValue(key, out var shot)) return;

        _activeShot = key;

        foreach (var (k, chip) in _shotChips)
        {
            var color = _shotColors[k];
            var isActive = k == key;
            chip.BackgroundColor = isActive ? color : Colors.Transparent;
            if (chip.Content is Label lbl)
                lbl.TextColor = isActive ? Colors.White : color;
        }

        PopulateStats(new LaneBallsRunResponse
        {
            BallPoints = shot.BallPoints,
            KinematicsTable = shot.KinematicsTable,
            Fps = shot.Fps
        });
        PopulatePlots(new LaneBallsRunResponse
        {
            BallPoints = shot.BallPoints,
            KinematicsTable = shot.KinematicsTable,
            Fps = shot.Fps
        });

        // Swap pose frames if pose data already loaded
        if (_poseShots.TryGetValue(key, out var poseShot) && poseShot.SkeletonPoints.Count > 0)
        {
            if (_isPlaying)
            {
                _isPlaying = false;
                _playbackTimer?.Stop();
                PlayStopButton.Text = "\u25B6";
            }
            _maxPoseFrameIndex = Math.Max(0, poseShot.SkeletonPoints.Count - 1);
            _poseFps = poseShot.Fps > 0 ? poseShot.Fps : 30f;
            FrameSlider.Maximum = _maxPoseFrameIndex;
            FrameSlider.Value = 0;
            PoseView.LoadFrames(poseShot.SkeletonPoints);
            PoseLoadingOverlay.IsVisible = false;
        }
    }

    private async Task LoadQueryPoseDataAsync(Task<FourDBodyQueryResponse?> poseTask)
    {
        try
        {
            var response = await poseTask;
            if (response?.Shots is { Count: > 0 })
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    _poseShots = response.Shots;
                    if (_activeShot is not null && _poseShots.TryGetValue(_activeShot, out var poseShot) && poseShot.SkeletonPoints.Count > 0)
                    {
                        _maxPoseFrameIndex = Math.Max(0, poseShot.SkeletonPoints.Count - 1);
                        _poseFps = poseShot.Fps > 0 ? poseShot.Fps : 30f;
                        FrameSlider.Maximum = _maxPoseFrameIndex;
                        FrameSlider.Value = 0;
                        PoseView.LoadFrames(poseShot.SkeletonPoints);
                        PoseLoadingOverlay.IsVisible = false;
                    }
                    else
                    {
                        PoseLoadingLabel.Text = "No pose data for this shot.";
                    }
                });
            }
            else
            {
                MainThread.BeginInvokeOnMainThread(() => PoseLoadingLabel.Text = "No pose data available.");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Pose query failed: " + ex);
            MainThread.BeginInvokeOnMainThread(() => PoseLoadingLabel.Text = "Pose estimation failed.");
        }
    }

    private async Task LoadPoseDataAsync(Task<FourDBodyRunResponse?> fourDBodyTask)
    {
        try
        {
            var poseResponse = await fourDBodyTask;

            if (poseResponse?.SkeletonPoints is { Count: > 0 })
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    _maxPoseFrameIndex = Math.Max(0, poseResponse.SkeletonPoints.Count - 1);
                    _poseFps = poseResponse.Fps > 0 ? poseResponse.Fps : 30f;
                    FrameSlider.Maximum = _maxPoseFrameIndex;
                    FrameSlider.Value = 0;

                    PoseView.LoadFrames(poseResponse.SkeletonPoints);

                    PoseLoadingOverlay.IsVisible = false;
                });
            }
            else
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    PoseLoadingLabel.Text = "No pose data available.";
                });
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Pose estimation failed: " + ex);
            MainThread.BeginInvokeOnMainThread(() =>
            {
                PoseLoadingLabel.Text = "Pose estimation failed.";
            });
        }
    }

    public static PopupOptions CreatePopupOptions()
    {
        return new PopupOptions
        {
            CanBeDismissedByTappingOutsideOfPopup = true,
            Shape = new RoundRectangle
            {
                CornerRadius = new CornerRadius(14),
                StrokeThickness = 0,
                Fill = Brush.Transparent
            }
        };
    }

    private static (double Width, double Height) ComputePopupSize(double fraction)
    {
        const double fallbackWidth = 460;
        const double fallbackHeight = 820;

        // Hard ceiling so the popup doesn't blow up on large desktop windows.
        // Beyond this size the stat cards balloon out of proportion.
        // Mobile-first ceilings: the popup is sized as a fraction of the
        // window/display, then clamped. The ceilings keep desktop windows
        // sane; the floors keep tiny windows readable.
        // Heights are intentionally conservative so the popup never exceeds
        // the visible viewport on a phone (status bar + nav bar eat ~10%).
        const double maxWidth = 600;
        const double maxHeight = 780;
        // Floors are intentionally low so the popup can collapse to whatever
        // the viewport actually provides on tiny phones. The card font scaler
        // in OnStatBoxSizeChanged takes care of legibility at small widths.
        const double minWidth = 240;
        const double minHeight = 420;

        double w, h;

        // Window.Width/Height are already in DIPs — no density conversion needed.
        var window = Application.Current?.Windows?.FirstOrDefault();
        if (window is { Width: > 0, Height: > 0 })
        {
            w = window.Width * fraction;
            h = window.Height * fraction;
        }
        else
        {
            // Fallback: DeviceDisplay reports raw pixels, so divide by density.
            var info = DeviceDisplay.MainDisplayInfo;
            if (info.Width > 0 && info.Height > 0)
            {
                var density = info.Density > 0 ? info.Density : 1;
                w = info.Width / density * fraction;
                h = info.Height / density * fraction;
            }
            else
            {
                w = fallbackWidth;
                h = fallbackHeight;
            }
        }

        return (Math.Clamp(w, minWidth, maxWidth), Math.Clamp(h, minHeight, maxHeight));
    }

    private void PopulateStats(LaneBallsRunResponse response)
    {
        var pts = response.BallPoints;
        var kin = response.KinematicsTable;

        if (pts.Count > 0)
        {
            var first = pts[0];
            var last = pts[^1];
            StatEntryX.Text = $"{first.X * MetersToInches:F1}";
            StatExitX.Text = $"{last.X * MetersToInches:F1}";

            StatLaunchAngle.Text = $"{ComputeLaunchAngle(pts):F1}";
            StatEntryAngle.Text = $"{ComputeEntryAngle(pts):F1}";
            StatBreakpoint.Text = $"{ComputeBreakpointDistance(pts) * MetersToFeet:F1}";
            StatTotalHook.Text = $"{ComputeTotalHook(pts) * MetersToInches:F1}";
        }

        if (kin.Count > 0)
        {
            var avgSpeed = kin.Average(k => k.MeanSpeedMps) * MpsToMph;
            // Sensor reports magnitude; ball is always decelerating under friction,
            // so flip the sign for display so users see "-X.X ft/s²" not "X.X".
            var avgAccel = -Math.Abs(kin.Average(k => k.MeanAccelerationMps2) * Mps2ToFtps2);
            var entrySpeed = kin[0].MeanSpeedMps * MpsToMph;
            var exitSpeed = kin[^1].MeanSpeedMps * MpsToMph;

            StatAvgSpeed.Text = $"{avgSpeed:F1}";
            StatAvgAccel.Text = $"{avgAccel:F1}";
            StatEntrySpeed.Text = $"{entrySpeed:F1}";
            StatExitSpeed.Text = $"{exitSpeed:F1}";
        }
    }

    private void PopulatePlots(LaneBallsRunResponse response)
    {
        var kin = response.KinematicsTable;

        if (kin.Count > 0)
        {
            var speedValues = kin.Select(k => (float)(k.MeanSpeedMps * MpsToMph)).ToArray();
            var speedLabels = kin.Select(k => $"Q{k.Quarter}").ToArray();
            SpeedPlotView.Drawable = new CiclopesBarPlotDrawable(speedValues, speedLabels,
                Color.FromArgb("#355070"), Color.FromArgb("#FA8847"));

            var accelValues = kin.Select(k => -Math.Abs((float)(k.MeanAccelerationMps2 * Mps2ToFtps2))).ToArray();
            var accelLabels = kin.Select(k => $"Q{k.Quarter}").ToArray();
            AccelPlotView.Drawable = new CiclopesBarPlotDrawable(accelValues, accelLabels,
                Color.FromArgb("#4A6D90"), Color.FromArgb("#FA8847"));
        }

        var pts = response.BallPoints;
        if (pts.Count > 1)
        {
            // Convert lateral positions from meters to inches for the plot
            var imperialPts = pts.Select(p => new CiclopesBallPoint
            {
                X = p.X * MetersToInches,
                Y = p.Y * MetersToFeet
            }).ToList();
            LateralPlotView.Drawable = new CiclopesLinePlotDrawable(imperialPts);
        }
    }

    // ── Ball trajectory analysis ──

    private const double LaneCenterX = 1.0541 / 2.0; // meters

    /// <summary>
    /// Finds the breakpoint — the down-lane distance (Y) at which the ball reaches
    /// its maximum lateral displacement from the lane center before hooking back.
    /// Returns the Y distance in meters from the foul line.
    /// </summary>
    private static double ComputeBreakpointDistance(IReadOnlyList<CiclopesBallPoint> pts)
    {
        if (pts.Count == 0) return 0;

        var breakpointIdx = 0;
        var maxDeviation = 0.0;

        for (var i = 0; i < pts.Count; i++)
        {
            var deviation = Math.Abs(pts[i].X - LaneCenterX);
            if (deviation > maxDeviation)
            {
                maxDeviation = deviation;
                breakpointIdx = i;
            }
        }

        return pts[breakpointIdx].Y;
    }

    /// <summary>
    /// Computes the entry angle in degrees — the angle between the ball's direction
    /// of travel and the lane axis (Y) as it approaches the pins.
    /// Uses the trajectory from the breakpoint to the final point:
    ///   angle = arctan(|deltaX| / deltaY)
    /// where deltaX is the lateral movement and deltaY is the forward movement
    /// from the breakpoint to the pin end.
    /// </summary>
    private static double ComputeEntryAngle(IReadOnlyList<CiclopesBallPoint> pts)
        => ComputeWindowedAngleDeg(pts, fromEnd: true);

    /// <summary>
    /// Launch angle in degrees — angle off the lane axis at release. Uses a
    /// least-squares fit over the first window of points so the value isn't
    /// dominated by a single noisy sample.
    /// </summary>
    private static double ComputeLaunchAngle(IReadOnlyList<CiclopesBallPoint> pts)
        => ComputeWindowedAngleDeg(pts, fromEnd: false);

    /// <summary>
    /// Total hook — peak lateral deviation from the release X position, in meters.
    /// Captures how much the ball curves regardless of where the release was.
    /// </summary>
    private static double ComputeTotalHook(IReadOnlyList<CiclopesBallPoint> pts)
    {
        if (pts.Count == 0) return 0;
        var startX = pts[0].X;
        var maxDev = 0.0;
        for (var i = 1; i < pts.Count; i++)
        {
            var dev = Math.Abs(pts[i].X - startX);
            if (dev > maxDev) maxDev = dev;
        }
        return maxDev;
    }

    /// <summary>
    /// Fits a line X = m·Y + b through a window at the start (fromEnd=false)
    /// or end (fromEnd=true) of the trajectory, and returns |atan(m)| in degrees.
    /// Window is the larger of 4 samples or 15% of the trajectory.
    /// </summary>
    private static double ComputeWindowedAngleDeg(IReadOnlyList<CiclopesBallPoint> pts, bool fromEnd)
    {
        if (pts.Count < 2) return 0;

        var window = Math.Max(4, (int)Math.Round(pts.Count * 0.15));
        window = Math.Min(window, pts.Count);

        var startIdx = fromEnd ? pts.Count - window : 0;
        var endIdx = fromEnd ? pts.Count : window;

        double sumY = 0, sumX = 0, sumYY = 0, sumXY = 0;
        var n = endIdx - startIdx;
        for (var i = startIdx; i < endIdx; i++)
        {
            sumY += pts[i].Y;
            sumX += pts[i].X;
            sumYY += pts[i].Y * pts[i].Y;
            sumXY += pts[i].Y * pts[i].X;
        }

        var denom = n * sumYY - sumY * sumY;
        if (Math.Abs(denom) < 1e-9) return 0;

        var slope = (n * sumXY - sumY * sumX) / denom; // dX/dY
        return Math.Atan(Math.Abs(slope)) * (180.0 / Math.PI);
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

    /// <summary>
    /// Scales the font size of the value/unit labels inside a stat card to a
    /// percentage of the card's actual pixel height, so the numbers stay
    /// readable across phone, tablet, and desktop popup widths.
    /// </summary>
    private void OnStatBoxSizeChanged(object? sender, EventArgs e)
    {
        if (sender is not Grid box || box.Height <= 0 || box.Width <= 0) return;

        var valueFont = Math.Clamp(box.Height * 0.58, 12, 26);
        var unitFont = Math.Clamp(valueFont * 0.55, 9, 14);

        // Value + unit share one centered row: "-12.3 mph" ≈ 8 glyphs at ~0.58em.
        var widthCap = box.Width / (8 * 0.58);
        valueFont = Math.Min(valueFont, Math.Max(11, widthCap));
        unitFont = Math.Min(unitFont, valueFont * 0.6);

        foreach (var child in box.Children)
        {
            if (child is not Label label) continue;
            var col = Grid.GetColumn(label);
            label.FontSize = col == 0 ? valueFont : unitFont;
        }
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
        var frame = (int)Math.Round(e.NewValue);
        PoseView.SetFrame(frame);
    }

    private void OnSpeedPickerChanged(object? sender, EventArgs e)
    {
        var idx = SpeedPicker.SelectedIndex;
        if (idx < 0 || idx >= SpeedMultipliers.Length) return;

        _speedMultiplier = SpeedMultipliers[idx];
        UpdateTimerInterval();
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
            UpdateTimerInterval();
            _playbackTimer.Start();
            PlayStopButton.Text = "\u25A0";
        }
    }

    private void UpdateTimerInterval()
    {
        if (_playbackTimer == null) return;
        var intervalMs = 1000.0 / (_poseFps * _speedMultiplier);
        _playbackTimer.Interval = TimeSpan.FromMilliseconds(intervalMs);
    }

    private IDispatcherTimer CreatePlaybackTimer()
    {
        var timer = Dispatcher.CreateTimer();
        var intervalMs = 1000.0 / (_poseFps * _speedMultiplier);
        timer.Interval = TimeSpan.FromMilliseconds(intervalMs);
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
