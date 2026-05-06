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

    // Centripetal Catmull-Rom alpha. 0.5 avoids the loops/cusps that uniform
    // Catmull-Rom can create when ball samples are unevenly spaced.
    private const float SplineAlpha = 0.5f;
    private const float MinSplinePointDistancePixels = 0.75f;

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

        // NOTE: canvas.SetShadow is unreliable on Android GraphicsView (renders
        // as a solid opaque blob or not at all). Approximate the drop shadow
        // by stacking two translucent offset rects instead — looks identical
        // on all platforms.
        var compositeRect = new RectF(compositeLeft, laneTop, totalWidth, laneHeight);
        canvas.FillColor = Color.FromArgb("#30000000");
        canvas.FillRoundedRectangle(
            new RectF(compositeRect.Left, compositeRect.Top + 3f, compositeRect.Width, compositeRect.Height),
            3f);
        canvas.FillColor = Color.FromArgb("#20000000");
        canvas.FillRoundedRectangle(
            new RectF(compositeRect.Left - 1f, compositeRect.Top + 5f, compositeRect.Width + 2f, compositeRect.Height),
            3f);

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

            var splinePoints = FilterSplinePoints(screen, MinSplinePointDistancePixels);
            if (splinePoints.Count < 2) continue;

            canvas.StrokeColor = color;
            canvas.DrawPath(BuildCatmullRomPath(splinePoints, SplineAlpha));
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

        // Android's GraphicsView backend can wash out LinearGradientPaint fills
        // here, so use deterministic alpha bands for the same polished look.
        DrawHorizontalGradientBands(
            canvas,
            laneRect,
            Color.FromArgb("#30FFFFFF"),
            Color.FromArgb("#30000000"),
            28);
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
        DrawVerticalGradientBands(
            canvas,
            rect,
            leftSide ? Color.FromArgb("#FFB8BDC2") : Color.FromArgb("#FF4A4F55"),
            leftSide ? Color.FromArgb("#FF4A4F55") : Color.FromArgb("#FFB8BDC2"),
            18);

        // Soft top highlight to suggest a glossy rim.
        var glossHeight = rect.Height * 0.25f;
        DrawHorizontalGradientBands(
            canvas,
            new RectF(rect.Left, rect.Top, rect.Width, glossHeight),
            Color.FromArgb("#50FFFFFF"),
            Color.FromArgb("#00FFFFFF"),
            8);

        // Crisp inner edge against the lane.
        canvas.StrokeColor = Color.FromArgb("#FF2C3034");
        canvas.StrokeSize = 1f;
        var edgeX = leftSide ? rect.Right : rect.Left;
        canvas.DrawLine(edgeX, rect.Top, edgeX, rect.Bottom);
    }

    private static void DrawHorizontalGradientBands(
        ICanvas canvas,
        RectF rect,
        Color startColor,
        Color endColor,
        int steps)
    {
        if (steps <= 0 || rect.Width <= 0f || rect.Height <= 0f)
        {
            return;
        }

        var bandHeight = rect.Height / steps;
        for (var i = 0; i < steps; i++)
        {
            var t = steps == 1 ? 0f : i / (float)(steps - 1);
            var top = rect.Top + i * bandHeight;
            var height = i == steps - 1 ? rect.Bottom - top : bandHeight + 0.5f;
            canvas.FillColor = Lerp(startColor, endColor, t);
            canvas.FillRectangle(rect.Left, top, rect.Width, height);
        }
    }

    private static void DrawVerticalGradientBands(
        ICanvas canvas,
        RectF rect,
        Color startColor,
        Color endColor,
        int steps)
    {
        if (steps <= 0 || rect.Width <= 0f || rect.Height <= 0f)
        {
            return;
        }

        var bandWidth = rect.Width / steps;
        for (var i = 0; i < steps; i++)
        {
            var t = steps == 1 ? 0f : i / (float)(steps - 1);
            var left = rect.Left + i * bandWidth;
            var width = i == steps - 1 ? rect.Right - left : bandWidth + 0.5f;
            canvas.FillColor = Lerp(startColor, endColor, t);
            canvas.FillRectangle(left, rect.Top, width, rect.Height);
        }
    }

    private static Color Lerp(Color start, Color end, float amount)
    {
        return new Color(
            start.Red + ((end.Red - start.Red) * amount),
            start.Green + ((end.Green - start.Green) * amount),
            start.Blue + ((end.Blue - start.Blue) * amount),
            start.Alpha + ((end.Alpha - start.Alpha) * amount));
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
    /// Removes non-finite and visually duplicate points before spline fitting.
    /// Near-duplicate samples are common at the start of a shot and make
    /// Catmull-Rom endpoint tangents unstable enough to show as a small cusp.
    /// </summary>
    private static List<PointF> FilterSplinePoints(IReadOnlyList<PointF> pts, float minDistancePixels)
    {
        var filtered = new List<PointF>(pts.Count);
        var minDistanceSquared = minDistancePixels * minDistancePixels;

        foreach (var pt in pts)
        {
            if (!float.IsFinite(pt.X) || !float.IsFinite(pt.Y))
            {
                continue;
            }

            if (filtered.Count == 0 || DistanceSquared(filtered[^1], pt) >= minDistanceSquared)
            {
                filtered.Add(pt);
            }
            else
            {
                // Keep the most recent location for sub-pixel clusters so the
                // rendered line still ends at the newest detection.
                filtered[^1] = pt;
            }
        }

        return filtered;
    }

    /// <summary>
    /// Builds a smooth centripetal Catmull-Rom spline through the control
    /// points. The older uniform Catmull-Rom Bezier conversion can overshoot
    /// badly with unevenly spaced samples, especially at release when several
    /// detections may be packed into the same rendered pixel.
    /// </summary>
    private static PathF BuildCatmullRomPath(IReadOnlyList<PointF> pts, float alpha)
    {
        var path = new PathF();
        path.MoveTo(pts[0]);

        if (pts.Count == 2)
        {
            path.LineTo(pts[1]);
            return path;
        }

        for (var i = 0; i < pts.Count - 1; i++)
        {
            var p0 = i == 0 ? ReflectPoint(pts[0], pts[1]) : pts[i - 1];
            var p1 = pts[i];
            var p2 = pts[i + 1];
            var p3 = i + 2 < pts.Count ? pts[i + 2] : ReflectPoint(pts[i + 1], pts[i]);

            AddCentripetalSegment(path, p0, p1, p2, p3, alpha);
        }

        return path;
    }

    private static void AddCentripetalSegment(
        PathF path,
        PointF p0,
        PointF p1,
        PointF p2,
        PointF p3,
        float alpha)
    {
        var t0 = 0f;
        var t1 = GetKnot(t0, p0, p1, alpha);
        var t2 = GetKnot(t1, p1, p2, alpha);
        var t3 = GetKnot(t2, p2, p3, alpha);

        if (t2 <= t1)
        {
            path.LineTo(p2);
            return;
        }

        var segmentLength = MathF.Sqrt(DistanceSquared(p1, p2));
        var steps = Math.Clamp((int)MathF.Ceiling(segmentLength / 6f), 6, 24);
        for (var step = 1; step <= steps; step++)
        {
            var t = t1 + (t2 - t1) * step / steps;
            path.LineTo(InterpolateCentripetal(p0, p1, p2, p3, t0, t1, t2, t3, t));
        }
    }

    private static PointF InterpolateCentripetal(
        PointF p0,
        PointF p1,
        PointF p2,
        PointF p3,
        float t0,
        float t1,
        float t2,
        float t3,
        float t)
    {
        var a1 = Interpolate(p0, p1, t0, t1, t);
        var a2 = Interpolate(p1, p2, t1, t2, t);
        var a3 = Interpolate(p2, p3, t2, t3, t);
        var b1 = Interpolate(a1, a2, t0, t2, t);
        var b2 = Interpolate(a2, a3, t1, t3, t);

        return Interpolate(b1, b2, t1, t2, t);
    }

    private static PointF Interpolate(PointF a, PointF b, float ta, float tb, float t)
    {
        var span = tb - ta;
        if (span <= float.Epsilon)
        {
            return b;
        }

        var weightA = (tb - t) / span;
        var weightB = (t - ta) / span;
        return new PointF(
            a.X * weightA + b.X * weightB,
            a.Y * weightA + b.Y * weightB);
    }

    private static float GetKnot(float previous, PointF a, PointF b, float alpha)
    {
        var distanceSquared = DistanceSquared(a, b);
        if (distanceSquared <= float.Epsilon)
        {
            return previous;
        }

        return previous + MathF.Pow(distanceSquared, alpha * 0.5f);
    }

    private static PointF ReflectPoint(PointF through, PointF point)
    {
        return new PointF(
            through.X + (through.X - point.X),
            through.Y + (through.Y - point.Y));
    }

    private static float DistanceSquared(PointF a, PointF b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return dx * dx + dy * dy;
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
