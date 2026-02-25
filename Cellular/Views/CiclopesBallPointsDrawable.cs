using Cellular.Cloud_API.Models;

namespace Cellular.Views;

public class CiclopesBallPointsDrawable : IDrawable
{
    private const float DefaultLaneLengthMeters = 18.288f;
    private const float LaneWidthMeters = 1.0668f;
    private readonly float _laneLengthMeters;
    private readonly float _laneStartMeters;

    public IReadOnlyList<CiclopesBallPoint> BallPoints { get; set; } = [];

    public CiclopesBallPointsDrawable(IReadOnlyList<CiclopesBallPoint> ballPoints)
    {
        BallPoints = ballPoints;
        (_laneStartMeters, _laneLengthMeters) = ComputeRenderWindow(ballPoints);
    }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        canvas.SaveState();
        canvas.Antialias = true;

        // Clear background transparent so the modal bg shows through
        canvas.FillColor = Colors.Transparent;
        canvas.FillRectangle(dirtyRect);

        // Compute lane rect — full height, aspect-correct width, centered
        const float padY = 12f;
        var availableWidth = dirtyRect.Width;
        var availableHeight = dirtyRect.Height - padY * 2f;
        var laneAspect = LaneWidthMeters / _laneLengthMeters;

        var laneHeight = availableHeight;
        var laneWidth = laneHeight * laneAspect;

        if (laneWidth > availableWidth)
        {
            laneWidth = availableWidth;
            laneHeight = laneWidth / laneAspect;
        }

        var laneRect = new RectF(
            dirtyRect.Center.X - (laneWidth / 2f),
            dirtyRect.Center.Y - (laneHeight / 2f),
            laneWidth,
            laneHeight
        );

        // Gutter — drawn snugly around the lane only
        const float gutterPad = 8f;
        var gutterRect = new RectF(
            laneRect.Left - gutterPad,
            laneRect.Top,
            laneRect.Width + gutterPad * 2f,
            laneRect.Height
        );
        canvas.FillColor = Color.FromArgb("#b0b0b0");
        canvas.FillRectangle(gutterRect);

        // Lane surface — sharp corners
        canvas.FillColor = Color.FromArgb("#c69c6d");
        canvas.FillRectangle(laneRect);
        canvas.StrokeColor = Color.FromArgb("#8B5A2B");
        canvas.StrokeSize = 2f;
        canvas.DrawRectangle(laneRect);

        // Ball points
        canvas.FillColor = Colors.Red;
        foreach (var point in BallPoints)
        {
            var normalizedX = (float)Math.Clamp(point.X / LaneWidthMeters, 0d, 1d);
            var normalizedY = (float)Math.Clamp((point.Y - _laneStartMeters) / _laneLengthMeters, 0d, 1d);

            var x = laneRect.Left + (normalizedX * laneRect.Width);
            var y = laneRect.Bottom - (normalizedY * laneRect.Height);

            canvas.FillCircle(x, y, 3.5f);
        }

        canvas.RestoreState();
    }

    private static (float laneStartMeters, float laneLengthMeters) ComputeRenderWindow(IReadOnlyList<CiclopesBallPoint> points)
    {
        if (points.Count == 0)
        {
            return (0f, DefaultLaneLengthMeters);
        }

        var minY = points.Min(p => p.Y);
        var maxY = points.Max(p => p.Y);
        var span = maxY - minY;

        var renderLength = (float)Math.Clamp(span + 1.0d, 3.0d, 8.0d);
        var start = (float)Math.Max(0d, minY - 0.5d);
        var maxStart = Math.Max(0d, DefaultLaneLengthMeters - renderLength);
        start = Math.Min(start, (float)maxStart);

        return (start, renderLength);
    }
}
