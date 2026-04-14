using System.Reflection;
using AndroidX.Camera.Core;
using AndroidX.Camera.Lifecycle;
using AndroidX.Camera.Video;
using CommunityToolkit.Maui.Views;
using Java.Util.Concurrent;

namespace Cellular;

/// <summary>
/// Rebuilds the CameraX Recorder + VideoCapture before each second-plus recording.
///
/// Root cause:
///   PlatformStopVideoRecording calls CleanupVideoRecordingResources() which nulls
///   videoRecording/videoRecordingFile/videoRecordingFinalizeTcs — but leaves the same
///   Recorder and VideoCapture instances alive. That Recorder's MediaCodec audio encoder
///   went through STARTED → STOPPING during the stop (triggered by MainActivity pausing
///   when the FileSaver dialog opens). Even after it reports RELEASED at the software level,
///   the underlying hardware codec slot (c2.android.aac.encoder) is not fully freed by the
///   time PrepareRecording runs for the next session — resulting in error code 6 (C2_NO_MEMORY).
///
///   The only reliable fix is to dispose the stale Recorder (which holds the hardware slot)
///   and create a completely fresh one. The toolkit's private RebuildVideoCapture() method
///   does exactly this, but .NET for Android's linker removes private methods that have no
///   external callers even in Debug builds, so we cannot find it by name.
///
///   Instead, we replicate the same operations using field-level reflection:
///     1. Unbind old VideoCapture from ProcessCameraProvider (releases the pipeline surface)
///     2. Dispose old VideoCapture and Recorder (releases the Java objects + hardware encoder)
///     3. Build fresh Recorder.Builder().Build() and VideoCapture.WithOutput(recorder)
///     4. Write them back so PlatformStartVideoRecording picks them up, calls
///        BindVideoSessionAsync → RebindCamera → binds the fresh VideoCapture →
///        fresh audio encoder → no error 6
/// </summary>
public static class AndroidCameraReset
{
    public static void RebuildVideoCapture(CameraView cameraView)
    {
        try
        {
            var handler = cameraView.Handler;
            if (handler == null)
            {
                System.Diagnostics.Debug.WriteLine("[AndroidCameraReset] Handler is null — skipping");
                return;
            }

            // ── Step 1: get the internal cameraManager FIELD ──────────────────────────
            // The property (CameraManager) throws if null; use the backing field instead.
            var handlerType = handler.GetType();
            var managerField = handlerType.GetField("cameraManager",
                BindingFlags.NonPublic | BindingFlags.Instance);

            if (managerField == null)
            {
                // Walk up the type hierarchy in case of obfuscation/inheritance
                var t = handlerType.BaseType;
                while (t != null && managerField == null)
                {
                    managerField = t.GetField("cameraManager",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    t = t.BaseType;
                }
            }

            var manager = managerField?.GetValue(handler);
            if (manager == null)
            {
                System.Diagnostics.Debug.WriteLine("[AndroidCameraReset] cameraManager field not found or null");
                return;
            }

            var managerType = manager.GetType();

            // ── Step 2: read the fields we need ───────────────────────────────────────
            FieldInfo? FindField(string name)
            {
                var fi = managerType.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
                if (fi != null) return fi;
                // Walk base types
                var t = managerType.BaseType;
                while (t != null)
                {
                    fi = t.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
                    if (fi != null) return fi;
                    t = t.BaseType;
                }
                return null;
            }

            var cameraExecutorField    = FindField("cameraExecutor");
            var providerField          = FindField("processCameraProvider");
            var videoCaptureField      = FindField("videoCapture");
            var videoRecorderField     = FindField("videoRecorder");

            if (cameraExecutorField == null || videoCaptureField == null || videoRecorderField == null)
            {
                System.Diagnostics.Debug.WriteLine("[AndroidCameraReset] One or more required fields not found — logging all fields:");
                foreach (var f in managerType.GetFields(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public))
                    System.Diagnostics.Debug.WriteLine($"  field: {f.FieldType.Name} {f.Name}");
                return;
            }

            var cameraExecutor    = cameraExecutorField.GetValue(manager) as IExecutorService;
            var provider          = providerField?.GetValue(manager) as ProcessCameraProvider;
            var oldVideoCapture   = videoCaptureField.GetValue(manager) as VideoCapture;
            var oldVideoRecorder  = videoRecorderField.GetValue(manager) as Recorder;

            if (cameraExecutor == null)
            {
                System.Diagnostics.Debug.WriteLine("[AndroidCameraReset] cameraExecutor is null — cannot rebuild");
                return;
            }

            // ── Step 3: unbind + dispose old VideoCapture ─────────────────────────────
            if (oldVideoCapture != null)
            {
                try { provider?.Unbind(oldVideoCapture); } catch { /* ignore if not bound */ }
                oldVideoCapture.Dispose();
                videoCaptureField.SetValue(manager, null);
                System.Diagnostics.Debug.WriteLine("[AndroidCameraReset] Old VideoCapture unbound and disposed");
            }

            // ── Step 4: dispose old Recorder (releases hardware MediaCodec slot) ──────
            if (oldVideoRecorder != null)
            {
                oldVideoRecorder.Dispose();
                videoRecorderField.SetValue(manager, null);
                System.Diagnostics.Debug.WriteLine("[AndroidCameraReset] Old Recorder disposed");
            }

            // ── Step 5: build fresh Recorder + VideoCapture ───────────────────────────
            var recorderBuilder = new Recorder.Builder().SetExecutor(cameraExecutor);
            if (Quality.Highest != null)
                recorderBuilder = recorderBuilder.SetQualitySelector(QualitySelector.From(Quality.Highest));

            var newRecorder     = recorderBuilder.Build();
            var newVideoCapture = VideoCapture.WithOutput(newRecorder);

            videoRecorderField.SetValue(manager, newRecorder);
            videoCaptureField.SetValue(manager, newVideoCapture);

            System.Diagnostics.Debug.WriteLine("[AndroidCameraReset] Fresh Recorder + VideoCapture installed — ready for next recording");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AndroidCameraReset] Exception: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
