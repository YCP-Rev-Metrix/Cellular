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

        // ── Left foot ──
        (13, 15), // left ankle → left big toe
        (13, 16), // left ankle → left small toe
        (13, 17), // left ankle → left heel
        (15, 16), // big toe → small toe

        // ── Right foot ──
        (14, 18), // right ankle → right big toe
        (14, 19), // right ankle → right small toe
        (14, 20), // right ankle → right heel
        (18, 19), // big toe → small toe

        // ── Right hand (wrist=21) ──
        (21, 22), (22, 23), (23, 24), (24, 25),         // thumb
        (21, 26), (26, 27), (27, 28), (28, 29),         // index
        (21, 30), (30, 31), (31, 32), (32, 33),         // middle
        (21, 34), (34, 35), (35, 36), (36, 37),         // ring
        (21, 38), (38, 39), (39, 40), (40, 41),         // pinky

        // ── Left hand (wrist=42) ──
        (42, 43), (43, 44), (44, 45), (45, 46),         // thumb
        (42, 47), (47, 48), (48, 49), (49, 50),         // index
        (42, 51), (51, 52), (52, 53), (53, 54),         // middle
        (42, 55), (55, 56), (56, 57), (57, 58),         // ring
        (42, 59), (59, 60), (60, 61), (61, 62),         // pinky

        // ── Extras: anatomical detail ──
        (8, 63),  // right elbow → right olecranon
        (7, 64),  // left elbow → left olecranon
        (8, 65),  // right elbow → right cubital fossa
        (7, 66),  // left elbow → left cubital fossa
        (6, 67),  // right shoulder → right acromion
        (5, 68),  // left shoulder → left acromion
    ];

    /// <summary>
    /// Color by body region for a given JointId.
    /// </summary>
    public static string GetJointColor(int jointId) => jointId switch
    {
        >= 0 and <= 4 => "#FF6B6B",     // Head: coral
        5 or 7 or 42 or 64 or 66 or 68 => "#2EC4B6",  // Left arm side: teal
        6 or 8 or 21 or 63 or 65 or 67 => "#FF9F1C",  // Right arm side: orange
        9 or 11 or 13 or >= 15 and <= 17 => "#4A90D9", // Left leg: blue
        10 or 12 or 14 or >= 18 and <= 20 => "#6BCB77",// Right leg: green
        >= 22 and <= 41 => "#FF9F1C",   // Right hand: orange
        >= 43 and <= 62 => "#2EC4B6",   // Left hand: teal
        69 => "#7C6BC4",                 // Neck: purple
        _ => "#AAAAAA"
    };

    public static string GetBoneColor(int jointA, int jointB)
    {
        return GetJointColor(Math.Min(jointA, jointB));
    }
}
