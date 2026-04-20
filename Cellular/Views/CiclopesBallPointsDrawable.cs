using Cellular.Cloud_API.Models;

namespace Cellular.Views;

public class CiclopesBallPointsDrawable : IDrawable
{
    // Actual lane coordinate system bounds
    private const float LaneWidthMeters = 1.0541f;
    private const float LaneLengthMeters = 18.288f;

    // Visual exaggeration of the lane width — at true scale (1m wide × 18m long)
    // a hook curve looks like a thin sliver. Stretching the lateral axis ~4×
    // makes the trajectory shape readable while keeping the foul-line→pin
    // proportions intact for vertical reference marks.
    private const float LateralExaggeration = 3.0f;

    // Visual width of each gutter as a fraction of the (exaggerated) lane width.
    private const float GutterFraction = 0.12f;

    // Smoothing tension for the Catmull-Rom spline (0 = sharp, 0.5 = standard).
    private const float SplineTension = 0.5f;

    // Default single-shot stroke color — matches the purple used in the multi-shot palette.
    private static readonly Color SingleShotColor = Color.FromArgb("#8E44AD");

    private readonly float _renderStartY;
    private readonly float _renderLengthY;
    private readonly List<(Color Color, IReadOnlyList<CiclopesBallPoint> Points)> _series;

    public CiclopesBallPointsDrawable(IReadOnlyList<CiclopesBallPoint> ballPoints)
        : this([(SingleShotColor, ballPoints)])
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

        // Compute lane rect — full height, aspect-correct width, centered.
        // Reserve room for gutters on either side so the (gutter+lane+gutter)
        // composite fits inside dirtyRect at any aspect.
        const float padY = 12f;
        const float padLeft = 22f;   // extra breathing room on the left
        const float padRight = 8f;
        var availableWidth = Math.Max(0, dirtyRect.Width - padLeft - padRight);
        var availableHeight = Math.Max(0, dirtyRect.Height - padY * 2f);
        var laneAspect = (LaneWidthMeters * LateralExaggeration) / _renderLengthY;

        // Total horizontal footprint = laneWidth * (1 + 2*GutterFraction).
        var totalAspect = laneAspect * (1f + 2f * GutterFraction);

        var laneHeight = availableHeight;
        var totalWidth = laneHeight * totalAspect;

        if (totalWidth > availableWidth)
        {
            totalWidth = availableWidth;
            laneHeight = totalWidth / totalAspect;
        }

        var laneWidth = totalWidth / (1f + 2f * GutterFraction);
        var gutterPad = laneWidth * GutterFraction;

        // Center the (gutter+lane+gutter) composite inside the available area,
        // anchored to the left padding.
        var compositeLeft = dirtyRect.Left + padLeft + (availableWidth - totalWidth) / 2f;
        var laneTop = dirtyRect.Top + padY + (availableHeight - laneHeight) / 2f;

        var laneRect = new RectF(
            compositeLeft + gutterPad,
            laneTop,
            laneWidth,
            laneHeight
        );

        // Soft drop shadow hugging the gutter+lane composite so it reads as a
        // physical object instead of being painted onto the popup background.
        var compositeRect = new RectF(compositeLeft, laneTop, totalWidth, laneHeight);
        canvas.SaveState();
        canvas.SetShadow(new SizeF(0, 3), 8f, Color.FromArgb("#80000000"));
        canvas.FillColor = Color.FromArgb("#FF000000");
        canvas.FillRoundedRectangle(compositeRect, 3f);
        canvas.RestoreState();

        DrawGutter(canvas, new RectF(compositeLeft, laneTop, gutterPad, laneHeight), leftSide: true);
        DrawGutter(canvas, new RectF(laneRect.Right, laneTop, gutterPad, laneHeight), leftSide: false);

        DrawLaneWoodPlanks(canvas, laneRect);

        canvas.StrokeColor = Color.FromArgb("#8B5A2B");
        canvas.StrokeSize = 2f;
        canvas.DrawRectangle(laneRect);

        DrawArrows(canvas, laneRect);

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

            // Project all points to screen space first so the spline operates
            // on the rendered geometry directly.
            var screen = new PointF[points.Count];
            for (var i = 0; i < points.Count; i++)
            {
                var nx = (float)(points[i].X / LaneWidthMeters);
                var ny = (float)((points[i].Y - _renderStartY) / _renderLengthY);
                screen[i] = new PointF(
                    laneRect.Left + nx * laneRect.Width,
                    laneRect.Bottom - ny * laneRect.Height);
            }

            canvas.StrokeColor = color;
            canvas.DrawPath(BuildCatmullRomPath(screen, SplineTension));
        }

        canvas.RestoreState();
    }

    /// <summary>
    /// Fills the lane with vertical wood-plank stripes and a subtle vignette
    /// so it doesn't read as a flat brown rectangle.
    /// </summary>
    private static void DrawLaneWoodPlanks(ICanvas canvas, RectF laneRect)
    {
        // Base maple fill.
        canvas.FillColor = Color.FromArgb("#c69c6d");
        canvas.FillRectangle(laneRect);

        // Planks run lengthwise — use 39 narrow vertical stripes alternating
        // tone slightly to suggest grain.
        const int plankCount = 39;
        var plankWidth = laneRect.Width / plankCount;
        var planks = new[]
        {
            Color.FromArgb("#cba578"),
            Color.FromArgb("#bf935f"),
            Color.FromArgb("#c79c6b"),
            Color.FromArgb("#b88a55"),
        };
        for (var i = 0; i < plankCount; i++)
        {
            canvas.FillColor = planks[i % planks.Length];
            canvas.FillRectangle(
                laneRect.Left + i * plankWidth,
                laneRect.Top,
                plankWidth + 0.5f, // overlap to avoid hairline gaps
                laneRect.Height);
        }

        // Faint grain lines between every plank.
        canvas.StrokeColor = Color.FromArgb("#40000000");
        canvas.StrokeSize = 0.5f;
        for (var i = 1; i < plankCount; i++)
        {
            var x = laneRect.Left + i * plankWidth;
            canvas.DrawLine(x, laneRect.Top, x, laneRect.Bottom);
        }

        // Top-down vignette so the surface looks slightly polished.
        var vignette = new LinearGradientPaint
        {
            StartColor = Color.FromArgb("#30FFFFFF"),
            EndColor = Color.FromArgb("#30000000"),
            StartPoint = new Point(0.5, 0),
            EndPoint = new Point(0.5, 1)
        };
        canvas.SetFillPaint(vignette, laneRect);
        canvas.FillRectangle(laneRect);
    }

    /// <summary>
    /// Draws a gutter with a soft inner shadow so it reads as a recessed channel.
    /// </summary>
    private static void DrawGutter(ICanvas canvas, RectF rect, bool leftSide)
    {
        // Base gutter color.
        canvas.FillColor = Color.FromArgb("#9aa0a6");
        canvas.FillRectangle(rect);

        // Concave shading — darker on the side adjacent to the lane, lighter
        // on the outer rim, simulating a curved channel under top lighting.
        var gradient = new LinearGradientPaint
        {
            StartColor = leftSide ? Color.FromArgb("#FFB8BDC2") : Color.FromArgb("#FF4A4F55"),
            EndColor = leftSide ? Color.FromArgb("#FF4A4F55") : Color.FromArgb("#FFB8BDC2"),
            StartPoint = new Point(0, 0.5),
            EndPoint = new Point(1, 0.5)
        };
        canvas.SetFillPaint(gradient, rect);
        canvas.FillRectangle(rect);

        // Soft top highlight to suggest a glossy rim.
        var topGloss = new LinearGradientPaint
        {
            StartColor = Color.FromArgb("#50FFFFFF"),
            EndColor = Color.FromArgb("#00FFFFFF"),
            StartPoint = new Point(0.5, 0),
            EndPoint = new Point(0.5, 0.25)
        };
        canvas.SetFillPaint(topGloss, rect);
        canvas.FillRectangle(rect);

        // Crisp inner edge against the lane.
        canvas.StrokeColor = Color.FromArgb("#FF2C3034");
        canvas.StrokeSize = 1f;
        var edgeX = leftSide ? rect.Right : rect.Left;
        canvas.DrawLine(edgeX, rect.Top, edgeX, rect.Bottom);
    }

    /// <summary>
    /// Draws the seven targeting arrows in their standard USBC positions —
    /// boards 5, 10, 15, 20, 25, 30, 35 with the V-formation distances
    /// (12/13/14/15/14/13/12 ft from the foul line). Skipped if the
    /// arrows fall outside the current render window.
    /// </summary>
    private void DrawArrows(ICanvas canvas, RectF laneRect)
    {
        const float ftToMeters = 0.3048f;
        const float laneBoards = 39f;
        var boardWidth = LaneWidthMeters / laneBoards;

        // (board, ft from foul line)
        ReadOnlySpan<(int Board, float Ft)> arrows =
        [
            (5,  12f),
            (10, 13f),
            (15, 14f),
            (20, 15f),
            (25, 14f),
            (30, 13f),
            (35, 12f),
        ];

        canvas.FillColor = Color.FromArgb("#3A2916");
        canvas.StrokeColor = Color.FromArgb("#1F1208");
        canvas.StrokeSize = 0.8f;

        var arrowH = laneRect.Height * 0.022f;
        var arrowW = laneRect.Width * 0.05f;

        foreach (var (board, ft) in arrows)
        {
            var yMeters = ft * ftToMeters;
            if (yMeters < _renderStartY || yMeters > _renderStartY + _renderLengthY)
                continue;

            var xMeters = (board - 0.5f) * boardWidth;
            var nx = xMeters / LaneWidthMeters;
            var ny = (yMeters - _renderStartY) / _renderLengthY;

            var cx = laneRect.Left + nx * laneRect.Width;
            var cy = laneRect.Bottom - ny * laneRect.Height;

            // Triangle pointing toward the pins (up-screen).
            var path = new PathF();
            path.MoveTo(cx, cy - arrowH);
            path.LineTo(cx - arrowW * 0.5f, cy + arrowH * 0.5f);
            path.LineTo(cx + arrowW * 0.5f, cy + arrowH * 0.5f);
            path.Close();

            canvas.FillPath(path);
            canvas.DrawPath(path);
        }
    }

    /// <summary>
    /// Builds a smooth Catmull-Rom spline through the given control points,
    /// converted to a sequence of cubic Bezier segments. Endpoint tangents are
    /// mirrored so the curve passes through the first and last sample without
    /// overshooting.
    /// </summary>
    private static PathF BuildCatmullRomPath(PointF[] pts, float tension)
    {
        var path = new PathF();
        path.MoveTo(pts[0]);

        if (pts.Length == 2)
        {
            path.LineTo(pts[1]);
            return path;
        }

        var s = (1f - tension) / 6f;

        for (var i = 0; i < pts.Length - 1; i++)
        {
            var p0 = i == 0 ? pts[0] : pts[i - 1];
            var p1 = pts[i];
            var p2 = pts[i + 1];
            var p3 = i + 2 < pts.Length ? pts[i + 2] : pts[i + 1];

            var c1 = new PointF(p1.X + s * (p2.X - p0.X), p1.Y + s * (p2.Y - p0.Y));
            var c2 = new PointF(p2.X - s * (p3.X - p1.X), p2.Y - s * (p3.Y - p1.Y));
            path.CurveTo(c1, c2, p2);
        }

        return path;
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
