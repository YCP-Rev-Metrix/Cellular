using Cellular.Cloud_API.Models;

namespace Cellular.Views;

public class CiclopesBallPointsDrawable : IDrawable
{
    // Actual lane coordinate system bounds
    private const float LaneWidthMeters = 1.0541f;
    private const float LaneLengthMeters = 18.288f;

    private readonly float _renderStartY;
    private readonly float _renderLengthY;
    private readonly List<(Color Color, IReadOnlyList<CiclopesBallPoint> Points)> _series;

    public CiclopesBallPointsDrawable(IReadOnlyList<CiclopesBallPoint> ballPoints)
        : this([(Colors.Red, ballPoints)])
    {
    }

    public CiclopesBallPointsDrawable(IReadOnlyList<(Color Color, IReadOnlyList<CiclopesBallPoint> Points)> series)
    {
        _series = [.. series];
        var allPoints = _series.SelectMany(s => s.Points).ToList();
        (_renderStartY, _renderLengthY) = ComputeRenderWindow(allPoints);
    }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        canvas.SaveState();
        canvas.Antialias = true;

        canvas.FillColor = Colors.Transparent;
        canvas.FillRectangle(dirtyRect);

        // Compute lane rect — full height, aspect-correct width, centered
        const float padY = 12f;
        var availableWidth = dirtyRect.Width;
        var availableHeight = dirtyRect.Height - padY * 2f;
        var laneAspect = LaneWidthMeters / _renderLengthY;

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

        const float gutterPad = 8f;
        var gutterRect = new RectF(
            laneRect.Left - gutterPad,
            laneRect.Top,
            laneRect.Width + gutterPad * 2f,
            laneRect.Height
        );
        canvas.FillColor = Color.FromArgb("#b0b0b0");
        canvas.FillRectangle(gutterRect);

        canvas.FillColor = Color.FromArgb("#c69c6d");
        canvas.FillRectangle(laneRect);
        canvas.StrokeColor = Color.FromArgb("#8B5A2B");
        canvas.StrokeSize = 2f;
        canvas.DrawRectangle(laneRect);

        // Faint dashed reference lines every 5 feet
        const float ftToMeters = 0.3048f;
        const float refIntervalMeters = 5f * ftToMeters;
        canvas.StrokeColor = Color.FromArgb("#55FFFFFF");
        canvas.StrokeSize = 1f;
        canvas.StrokeDashPattern = [3f, 4f];
        var firstMark = (float)Math.Ceiling(_renderStartY / refIntervalMeters) * refIntervalMeters;
        for (var y = firstMark; y < _renderStartY + _renderLengthY; y += refIntervalMeters)
        {
            var normY = (y - _renderStartY) / _renderLengthY;
            var screenY = laneRect.Bottom - normY * laneRect.Height;
            canvas.DrawLine(laneRect.Left, screenY, laneRect.Right, screenY);
        }
        canvas.StrokeDashPattern = null;

        // Draw each series
        canvas.StrokeLineCap = LineCap.Round;
        canvas.StrokeLineJoin = LineJoin.Round;
        canvas.StrokeSize = 2.5f;

        foreach (var (color, points) in _series)
        {
            if (points.Count < 2) continue;

            canvas.StrokeColor = color;
            var path = new PathF();

            var firstNormX = (float)(points[0].X / LaneWidthMeters);
            var firstNormY = (float)((points[0].Y - _renderStartY) / _renderLengthY);
            path.MoveTo(
                laneRect.Left + firstNormX * laneRect.Width,
                laneRect.Bottom - firstNormY * laneRect.Height);

            for (var i = 1; i < points.Count; i++)
            {
                var prevNormX = (float)(points[i - 1].X / LaneWidthMeters);
                var prevNormY = (float)((points[i - 1].Y - _renderStartY) / _renderLengthY);
                var currNormX = (float)(points[i].X / LaneWidthMeters);
                var currNormY = (float)((points[i].Y - _renderStartY) / _renderLengthY);

                var prevX = laneRect.Left + prevNormX * laneRect.Width;
                var prevY = laneRect.Bottom - prevNormY * laneRect.Height;
                var currX = laneRect.Left + currNormX * laneRect.Width;
                var currY = laneRect.Bottom - currNormY * laneRect.Height;

                var midY = (prevY + currY) / 2f;
                path.CurveTo(prevX, midY, currX, midY, currX, currY);
            }

            canvas.DrawPath(path);
        }

        canvas.RestoreState();
    }

    private static (float renderStartY, float renderLengthY) ComputeRenderWindow(IReadOnlyList<CiclopesBallPoint> points)
    {
        if (points.Count == 0)
        {
            return (0f, LaneLengthMeters);
        }

        var minY = points.Min(p => p.Y);
        var maxY = points.Max(p => p.Y);
        var span = maxY - minY;

        var renderLength = (float)Math.Max(span + 1.0d, 3.0d);
        var start = (float)Math.Max(0d, minY - 0.5d);

        if (start + renderLength > LaneLengthMeters)
        {
            start = Math.Max(0f, LaneLengthMeters - renderLength);
            renderLength = Math.Min(renderLength, LaneLengthMeters);
        }

        return (start, renderLength);
    }
}
