using Cellular.Cloud_API.Models;

namespace Cellular.Views;

public class CiclopesBallPointsDrawable : IDrawable
{
    // Actual lane coordinate system bounds
    private const float LaneWidthMeters = 1.0541f;
    private const float LaneLengthMeters = 18.288f;

    private readonly float _renderStartY;
    private readonly float _renderLengthY;

    public IReadOnlyList<CiclopesBallPoint> BallPoints { get; set; } = [];

    public CiclopesBallPointsDrawable(IReadOnlyList<CiclopesBallPoint> ballPoints)
    {
        BallPoints = ballPoints;
        (_renderStartY, _renderLengthY) = ComputeRenderWindow(ballPoints);
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

        // Ball path as a smooth line curve
        if (BallPoints.Count >= 2)
        {
            canvas.StrokeColor = Colors.Red;
            canvas.StrokeSize = 2.5f;
            canvas.StrokeLineCap = LineCap.Round;
            canvas.StrokeLineJoin = LineJoin.Round;

            var path = new PathF();

            var firstNormX = (float)(BallPoints[0].X / LaneWidthMeters);
            var firstNormY = (float)((BallPoints[0].Y - _renderStartY) / _renderLengthY);
            path.MoveTo(
                laneRect.Left + firstNormX * laneRect.Width,
                laneRect.Bottom - firstNormY * laneRect.Height);

            for (var i = 1; i < BallPoints.Count; i++)
            {
                var prevNormX = (float)(BallPoints[i - 1].X / LaneWidthMeters);
                var prevNormY = (float)((BallPoints[i - 1].Y - _renderStartY) / _renderLengthY);
                var currNormX = (float)(BallPoints[i].X / LaneWidthMeters);
                var currNormY = (float)((BallPoints[i].Y - _renderStartY) / _renderLengthY);

                var prevX = laneRect.Left + prevNormX * laneRect.Width;
                var prevY = laneRect.Bottom - prevNormY * laneRect.Height;
                var currX = laneRect.Left + currNormX * laneRect.Width;
                var currY = laneRect.Bottom - currNormY * laneRect.Height;

                // Cubic bezier with control points at midpoints for smooth curve
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

        // Pad 0.5m on each side, minimum 3m window, no upper cap
        var renderLength = (float)Math.Max(span + 1.0d, 3.0d);
        var start = (float)Math.Max(0d, minY - 0.5d);

        // Ensure window doesn't extend past lane end
        if (start + renderLength > LaneLengthMeters)
        {
            start = Math.Max(0f, LaneLengthMeters - renderLength);
            renderLength = Math.Min(renderLength, LaneLengthMeters);
        }

        return (start, renderLength);
    }
}
