using CellularCore.Rendering;

namespace Cellular.Views;

/// <summary>
/// Renders a single frame of MHR70 skeleton data as a 2D perspective projection.
/// Points stay in world-space (meters); Camera3D handles orbit + projection.
/// Auto-distances so the skeleton always fills the viewport.
/// </summary>
public class CiclopesSkeletonDrawable : IDrawable
{
    private readonly Camera3D _camera;

    public Dictionary<int, (float X, float Y, float Z)> Joints { get; set; } = new();
    public bool ShowJointLabels { get; set; }

    private readonly Dictionary<int, (float ScreenX, float ScreenY, float Depth)> _projected = new();

    public CiclopesSkeletonDrawable(Camera3D camera)
    {
        _camera = camera;
    }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        canvas.SaveState();
        canvas.Antialias = true;

        canvas.FillColor = Color.FromArgb("#1a1a2e");
        canvas.FillRectangle(dirtyRect);

        if (Joints.Count == 0)
        {
            canvas.FontSize = 13;
            canvas.FontColor = Colors.Gray;
            canvas.DrawString("No skeleton data",
                dirtyRect.Left, dirtyRect.Top, dirtyRect.Width, dirtyRect.Height,
                HorizontalAlignment.Center, VerticalAlignment.Center);
            canvas.RestoreState();
            return;
        }

        var cx = dirtyRect.Center.X;
        var cy = dirtyRect.Center.Y;
        var viewHalf = MathF.Min(dirtyRect.Width, dirtyRect.Height) / 2f;

        // Center at origin (meters)
        var allPts = Joints.Values.Select(p => (p.X, p.Y, p.Z)).ToList();
        var (centX, centY, centZ) = Camera3D.ComputeCentroid(allPts);

        var centered = new List<(float X, float Y, float Z)>(allPts.Count);
        foreach (var (px, py, pz) in allPts)
            centered.Add((px - centX, py - centY, pz - centZ));

        var boundingR = Camera3D.ComputeBoundingRadius(centered);

        // Auto-distance: skeleton fills ~65% of viewport
        _camera.Distance = Camera3D.ComputeOrbitDistance(boundingR);

        // viewportScale: maps world units to pixels via perspective
        // At distance D, a point at radius R projects to: viewportScale * R / D pixels
        // We want R to map to viewHalf * 0.65, so viewportScale = viewHalf * 0.65 * D / R = viewHalf * D * 0.65 / R
        // But since D = R / 0.65, this simplifies to viewportScale = viewHalf
        var viewportScale = viewHalf;

        // Project all joints
        _projected.Clear();
        var jointIds = Joints.Keys.ToList();
        foreach (var jointId in jointIds)
        {
            var (jx, jy, jz) = Joints[jointId];
            var proj = _camera.Project(
                jx - centX, jy - centY, jz - centZ,
                cx, cy, viewportScale);
            _projected[jointId] = proj;
        }

        // Ground grid
        DrawGroundGrid(canvas, centered, cx, cy, viewportScale);

        // Bones: only draw if both endpoints exist and the screen distance is reasonable
        var maxBoneScreen = viewHalf * 1.2f; // reject absurdly long bones

        var bonesToDraw = new List<(int A, int B, float AvgDepth)>();
        foreach (var (a, b) in SkeletonTopology.BoneConnections)
        {
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

            // Thinner lines for hand/finger bones
            var isFinger = (a >= 21 && a <= 62) || (b >= 21 && b <= 62);

            canvas.StrokeColor = Color.FromArgb(colorHex);
            canvas.StrokeSize = isFinger ? 1.5f : 2.5f;
            canvas.StrokeLineCap = LineCap.Round;
            canvas.DrawLine(pa.ScreenX, pa.ScreenY, pb.ScreenX, pb.ScreenY);
        }

        // Joints: farthest first
        var sortedJoints = _projected
            .OrderBy(kv => kv.Value.Depth)
            .ToList();

        foreach (var (jointId, (sx, sy, depth)) in sortedJoints)
        {
            var colorHex = SkeletonTopology.GetJointColor(jointId);
            var isFinger = jointId >= 22 && jointId <= 62;
            var radius = isFinger ? 2.5f : 4f;

            // Glow
            canvas.FillColor = Color.FromArgb(colorHex).WithAlpha(0.2f);
            canvas.FillCircle(sx, sy, radius + 2f);

            canvas.FillColor = Color.FromArgb(colorHex);
            canvas.FillCircle(sx, sy, radius);

            if (ShowJointLabels)
            {
                canvas.FontSize = 7;
                canvas.FontColor = Colors.White;
                canvas.DrawString(jointId.ToString(),
                    sx + radius + 2, sy - 4, 24, 10,
                    HorizontalAlignment.Left, VerticalAlignment.Center);
            }
        }

        canvas.RestoreState();
    }

    private void DrawGroundGrid(ICanvas canvas,
        IReadOnlyList<(float X, float Y, float Z)> centeredPts,
        float cx, float cy, float viewportScale)
    {
        canvas.StrokeColor = Color.FromArgb("#252545");
        canvas.StrokeSize = 0.5f;

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
            canvas.DrawLine(p1.ScreenX, p1.ScreenY, p2.ScreenX, p2.ScreenY);

            var p3 = _camera.Project(g, gridY, -extent, cx, cy, viewportScale);
            var p4 = _camera.Project(g, gridY, extent, cx, cy, viewportScale);
            canvas.DrawLine(p3.ScreenX, p3.ScreenY, p4.ScreenX, p4.ScreenY);
        }
    }
}
