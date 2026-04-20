using CellularCore.Rendering;
using SkiaSharp;

namespace Cellular.Views;

/// <summary>
/// GPU-accelerated skeleton renderer using SkiaSharp.
/// Renders a single frame of MHR70 skeleton data via SKCanvas.
/// </summary>
public class CiclopesSkeletonRenderer
{
    private readonly Camera3D _camera;

    public Dictionary<int, (float X, float Y, float Z)> Joints { get; set; } = new();
    public bool ShowJointLabels { get; set; }

    private readonly Dictionary<int, (float ScreenX, float ScreenY, float Depth)> _projected = new();

    // Pre-allocated paint objects to avoid GC pressure during draw
    private static readonly SKPaint BackgroundPaint = new() { Color = SKColor.Parse("#1a1a2e"), Style = SKPaintStyle.Fill };
    private static readonly SKPaint GridPaint = new() { Color = SKColor.Parse("#252545"), StrokeWidth = 0.5f, Style = SKPaintStyle.Stroke, IsAntialias = true };
    private static readonly SKPaint BonePaint = new() { StrokeWidth = 2.5f, Style = SKPaintStyle.Stroke, IsAntialias = true, StrokeCap = SKStrokeCap.Round };
    private static readonly SKPaint JointPaint = new() { Style = SKPaintStyle.Fill, IsAntialias = true };
    private static readonly SKPaint GlowPaint = new() { Style = SKPaintStyle.Fill, IsAntialias = true };
    private static readonly SKPaint TextPaint = new() { Color = SKColors.White, IsAntialias = true };
    private static readonly SKFont TextFont = new() { Size = 7 };
    private static readonly SKPaint NoDataPaint = new() { Color = SKColors.Gray, IsAntialias = true };
    private static readonly SKFont NoDataFont = new() { Size = 13 };

    // Color cache to avoid repeated parsing
    private static readonly Dictionary<string, SKColor> ColorCache = new();

    public CiclopesSkeletonRenderer(Camera3D camera)
    {
        _camera = camera;
    }

    private static SKColor GetCachedColor(string hex)
    {
        if (!ColorCache.TryGetValue(hex, out var color))
        {
            color = SKColor.Parse(hex);
            ColorCache[hex] = color;
        }
        return color;
    }

    public void Draw(SKCanvas canvas, int width, int height)
    {
        canvas.Clear(BackgroundPaint.Color);

        if (Joints.Count == 0)
        {
            canvas.DrawText("No skeleton data", width / 2f, height / 2f, SKTextAlign.Center, NoDataFont, NoDataPaint);
            return;
        }

        var cx = width / 2f;
        var cy = height / 2f;
        var viewHalf = MathF.Min(width, height) / 2f;

        // Center at origin (meters)
        var allPts = Joints.Values.Select(p => (p.X, p.Y, p.Z)).ToList();
        var (centX, centY, centZ) = Camera3D.ComputeCentroid(allPts);

        var centered = new List<(float X, float Y, float Z)>(allPts.Count);
        foreach (var (px, py, pz) in allPts)
            centered.Add((px - centX, py - centY, pz - centZ));

        var boundingR = Camera3D.ComputeBoundingRadius(centered);

        _camera.Distance = Camera3D.ComputeOrbitDistance(boundingR);

        var viewportScale = viewHalf;

        // Project all joints
        _projected.Clear();
        foreach (var jointId in Joints.Keys)
        {
            var (jx, jy, jz) = Joints[jointId];
            var proj = _camera.Project(
                jx - centX, jy - centY, jz - centZ,
                cx, cy, viewportScale);
            _projected[jointId] = proj;
        }

        // Ground grid
        DrawGroundGrid(canvas, centered, cx, cy, viewportScale);

        // Bones: depth-sorted
        var maxBoneScreen = viewHalf * 1.2f;

        var bonesToDraw = new List<(int A, int B, float AvgDepth)>();
        foreach (var (a, b) in SkeletonTopology.BoneConnections)
        {
            if (SkeletonTopology.ExcludedJoints.Contains(a) || SkeletonTopology.ExcludedJoints.Contains(b))
                continue;
            if (!_projected.TryGetValue(a, out var pa) || !_projected.TryGetValue(b, out var pb))
                continue;

            var dx = pa.ScreenX - pb.ScreenX;
            var dy = pa.ScreenY - pb.ScreenY;
            if (MathF.Sqrt(dx * dx + dy * dy) > maxBoneScreen)
                continue;

            bonesToDraw.Add((a, b, (pa.Depth + pb.Depth) / 2f));
        }
        bonesToDraw.Sort((x, y) => x.AvgDepth.CompareTo(y.AvgDepth));

        foreach (var (a, b, _) in bonesToDraw)
        {
            var pa = _projected[a];
            var pb = _projected[b];
            var colorHex = SkeletonTopology.GetBoneColor(a, b);

            BonePaint.Color = GetCachedColor(colorHex);
            canvas.DrawLine(pa.ScreenX, pa.ScreenY, pb.ScreenX, pb.ScreenY, BonePaint);
        }

        // Joints: farthest first, skip excluded
        var sortedJoints = _projected
            .Where(kv => !SkeletonTopology.ExcludedJoints.Contains(kv.Key))
            .OrderBy(kv => kv.Value.Depth)
            .ToList();

        foreach (var (jointId, (sx, sy, _)) in sortedJoints)
        {
            var colorHex = SkeletonTopology.GetJointColor(jointId);
            var color = GetCachedColor(colorHex);
            const float radius = 4f;

            // Glow
            GlowPaint.Color = color.WithAlpha(51); // ~0.2 alpha
            canvas.DrawCircle(sx, sy, radius + 2f, GlowPaint);

            JointPaint.Color = color;
            canvas.DrawCircle(sx, sy, radius, JointPaint);

            if (ShowJointLabels)
            {
                canvas.DrawText(jointId.ToString(), sx + radius + 2, sy + 3, TextFont, TextPaint);
            }
        }
    }

    private void DrawGroundGrid(SKCanvas canvas,
        IReadOnlyList<(float X, float Y, float Z)> centeredPts,
        float cx, float cy, float viewportScale)
    {
        var minY = centeredPts.Min(p => p.Y);
        var gridY = minY - 0.05f;

        var extent = Camera3D.ComputeBoundingRadius(centeredPts) * 1.2f;
        var steps = 8;
        var step = extent * 2f / steps;

        for (var i = 0; i <= steps; i++)
        {
            var g = -extent + i * step;

            var p1 = _camera.Project(-extent, gridY, g, cx, cy, viewportScale);
            var p2 = _camera.Project(extent, gridY, g, cx, cy, viewportScale);
            canvas.DrawLine(p1.ScreenX, p1.ScreenY, p2.ScreenX, p2.ScreenY, GridPaint);

            var p3 = _camera.Project(g, gridY, -extent, cx, cy, viewportScale);
            var p4 = _camera.Project(g, gridY, extent, cx, cy, viewportScale);
            canvas.DrawLine(p3.ScreenX, p3.ScreenY, p4.ScreenX, p4.ScreenY, GridPaint);
        }
    }
}
