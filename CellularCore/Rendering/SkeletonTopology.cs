namespace CellularCore.Rendering;

/// <summary>
/// MHR70 skeleton topology from SAM3DBody.
/// The 308 MHR keypoints are truncated to 70 (mhr_head.py:337 j3d[:, :70]).
/// joint_id == array index == MHR70 ordering from mhr70.py.
///
/// Mapping:
///   0-4   Head: nose(0), left_eye(1), right_eye(2), left_ear(3), right_ear(4)
///   5-8   Upper body: left_shoulder(5), right_shoulder(6), left_elbow(7), right_elbow(8)
///   9-14  Lower body: left_hip(9), right_hip(10), left_knee(11), right_knee(12),
///                      left_ankle(13), right_ankle(14)
///   15-20 Feet: left_big_toe(15), left_small_toe(16), left_heel(17),
///               right_big_toe(18), right_small_toe(19), right_heel(20)
///   21-41 Right hand: wrist(21), thumb(22-25), index(26-29), middle(30-33),
///                     ring(34-37), pinky(38-41)
///   42-62 Left hand:  wrist(42), thumb(43-46), index(47-50), middle(51-54),
///                     ring(55-58), pinky(59-62)
///   63-69 Extras: right_olecranon(63), left_olecranon(64), right_cubital_fossa(65),
///                 left_cubital_fossa(66), right_acromion(67), left_acromion(68), neck(69)
/// </summary>
public static class SkeletonTopology
{
    public static readonly (int A, int B)[] BoneConnections =
    [
        // ── Head ──
        (0, 1), (0, 2), (1, 3), (2, 4),

        // ── Neck to head/shoulders ──
        (69, 0),  // neck → nose
        (69, 5),  // neck → left shoulder
        (69, 6),  // neck → right shoulder

        // ── Torso frame ──
        (5, 6),   // shoulder → shoulder
        (5, 9),   // left shoulder → left hip
        (6, 10),  // right shoulder → right hip
        (9, 10),  // hip → hip

        // ── Left arm ──
        (5, 7),   // left shoulder → left elbow
        (7, 42),  // left elbow → left wrist

        // ── Right arm ──
        (6, 8),   // right shoulder → right elbow
        (8, 21),  // right elbow → right wrist

        // ── Left leg ──
        (9, 11),  // left hip → left knee
        (11, 13), // left knee → left ankle

        // ── Right leg ──
        (10, 12), // right hip → right knee
        (12, 14), // right knee → right ankle

    ];

    /// <summary>
    /// Joint IDs to exclude from rendering (hands, feet, extras).
    /// Wrists (21, 42) and ankles (13, 14) are kept as endpoints.
    /// </summary>
    public static readonly HashSet<int> ExcludedJoints =
    [
        // Feet
        15, 16, 17, 18, 19, 20,
        // Right hand fingers
        22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41,
        // Left hand fingers
        43, 44, 45, 46, 47, 48, 49, 50, 51, 52, 53, 54, 55, 56, 57, 58, 59, 60, 61, 62,
        // Extras (olecranon, cubital fossa, acromion)
        63, 64, 65, 66, 67, 68,
    ];

    /// <summary>
    /// Color by body region for a given JointId.
    /// </summary>
    public static string GetJointColor(int jointId) => jointId switch
    {
        >= 0 and <= 4 => "#FF6B6B",     // Head: coral
        5 or 7 or 42 => "#2EC4B6",      // Left arm side: teal
        6 or 8 or 21 => "#FF9F1C",      // Right arm side: orange
        9 or 11 or 13 => "#4A90D9",     // Left leg: blue
        10 or 12 or 14 => "#6BCB77",    // Right leg: green
        69 => "#7C6BC4",                 // Neck: purple
        _ => "#AAAAAA"
    };

    public static string GetBoneColor(int jointA, int jointB)
    {
        return GetJointColor(Math.Min(jointA, jointB));
    }
}
