namespace CellularCore.Rendering;

/// <summary>
/// Orbit camera for 3D skeleton viewing. The camera conceptually orbits
/// around the origin at a given distance while the skeleton stays centered.
///
/// Usage: center your points at the origin, call <see cref="Project"/> for each,
/// and use <see cref="ComputeOrbitDistance"/> to auto-fit the skeleton in the viewport.
/// </summary>
public class Camera3D
{
    /// <summary>Horizontal orbit angle in radians. Positive = camera moves right (object appears to rotate left).</summary>
    public float Azimuth { get; set; }

    /// <summary>Vertical orbit angle in radians. Positive = camera moves up (looking down at object).</summary>
    public float Elevation { get; set; }

    /// <summary>Distance from the camera to the orbit center (origin).</summary>
    public float Distance { get; set; } = 4f;

    private const float MaxElevation = 1.4f;  // ~80 degrees
    private const float MinElevation = -1.4f;

    public Camera3D(float azimuth = 0f, float elevation = 0.15f, float distance = 4f)
    {
        Azimuth = azimuth;
        Elevation = elevation;
        Distance = distance;
    }

    /// <summary>
    /// Update orbit angles from a drag delta (in pixels).
    /// Dragging right orbits camera right (skeleton appears to turn left).
    /// Dragging up orbits camera up (looking down at skeleton).
    /// </summary>
    public void Rotate(float deltaX, float deltaY, float sensitivity = 0.005f)
    {
        Azimuth -= deltaX * sensitivity;
        Elevation = Math.Clamp(Elevation + deltaY * sensitivity, MinElevation, MaxElevation);
    }

    /// <summary>
    /// Project a world-space point (centered at origin) to screen coordinates.
    /// The camera sits at distance D along the rotated Z-axis, looking at the origin.
    /// </summary>
    public (float ScreenX, float ScreenY, float Depth) Project(
        float x, float y, float z,
        float viewportCenterX, float viewportCenterY, float viewportScale)
    {
        // Rotate the point by the inverse of the camera orbit
        // (equivalent to the camera orbiting around the stationary object)

        // Ry(-azimuth): rotate around Y
        var cosA = MathF.Cos(-Azimuth);
        var sinA = MathF.Sin(-Azimuth);
        var rx = x * cosA + z * sinA;
        var ry = y;
        var rz = -x * sinA + z * cosA;

        // Rx(-elevation): rotate around X
        var cosE = MathF.Cos(-Elevation);
        var sinE = MathF.Sin(-Elevation);
        var fx = rx;
        var fy = ry * cosE - rz * sinE;
        var fz = ry * sinE + rz * cosE;

        // Now the camera is at (0, 0, -Distance) looking toward origin.
        // Transform to camera space: shift Z by Distance.
        var cz = fz + Distance;
        if (cz < 0.001f) cz = 0.001f;

        // Perspective projection (screen Y is flipped: positive Y = up in world, down on screen)
        var screenX = viewportCenterX + viewportScale * fx / cz;
        var screenY = viewportCenterY - viewportScale * fy / cz;

        return (screenX, screenY, fz);
    }

    /// <summary>
    /// Compute camera distance so a skeleton of given bounding radius
    /// fills approximately <paramref name="fillFraction"/> of the viewport.
    /// </summary>
    public static float ComputeOrbitDistance(float boundingRadius, float fillFraction = 0.65f)
    {
        if (boundingRadius < 0.001f) return 4f;
        // At distance D, a point at radius R projects to screenOffset = scale * R / D
        // We want screenOffset / (viewSize/2) = fillFraction
        // → D = R / fillFraction (when scale = viewSize/2)
        return boundingRadius / fillFraction;
    }

    /// <summary>
    /// Compute the centroid of a set of 3D points.
    /// </summary>
    public static (float CX, float CY, float CZ) ComputeCentroid(
        IReadOnlyList<(float X, float Y, float Z)> points)
    {
        if (points.Count == 0) return (0, 0, 0);

        float sx = 0, sy = 0, sz = 0;
        foreach (var (x, y, z) in points)
        {
            sx += x;
            sy += y;
            sz += z;
        }

        var n = (float)points.Count;
        return (sx / n, sy / n, sz / n);
    }

    /// <summary>
    /// Compute bounding radius of points (max distance from origin, assumed already centered).
    /// </summary>
    public static float ComputeBoundingRadius(IReadOnlyList<(float X, float Y, float Z)> points)
    {
        var maxR = 0f;
        foreach (var (x, y, z) in points)
        {
            var r = MathF.Sqrt(x * x + y * y + z * z);
            if (r > maxR) maxR = r;
        }
        return maxR < 0.001f ? 1f : maxR;
    }
}
