using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;

namespace Cellular.Services
{
    /// <summary>
    /// Data structure for sensor data points
    /// </summary>
    public class SensorDataPoint
    {
        public DateTime Timestamp { get; set; }
        public float? AccelX { get; set; }
        public float? AccelY { get; set; }
        public float? AccelZ { get; set; }
        public float? GyroX { get; set; }
        public float? GyroY { get; set; }
        public float? GyroZ { get; set; }
        public float? MagX { get; set; }
        public float? MagY { get; set; }
        public float? MagZ { get; set; }
        public float? Light { get; set; }
    }

    /// <summary>
    /// Manages a rolling buffer of sensor data that saves when light sensor goes high
    /// </summary>
    public class SensorBufferManager
    {
        private readonly IMetaWearService _metaWearService;
        private readonly List<SensorDataPoint> _sensorBuffer = new List<SensorDataPoint>();
        private readonly object _bufferLock = new object();
        private bool _isBuffering = false;
        private bool _hasTriggeredSave = false;
        private Timer? _bufferCleanupTimer;
        private string? _baseFileName;
        private bool _isContinuousSaving = false;
        private DateTime? _continuousSaveStartTime;
        private DateTime? _continuousSaveEndTime;
        private Timer? _continuousSaveTimer;
        private readonly List<SensorDataPoint> _allSavedPoints = new List<SensorDataPoint>();
        
        // Accelerometer derivative tracking
        private float? _prevAccelX;
        private float? _prevAccelY;
        private float? _prevAccelZ;
        private DateTime? _prevAccelTimestamp;
        private readonly List<DateTime> _accelerometerJumpTimestamps = new List<DateTime>();
        private bool _hasPrintedFirstJump = false;
        private readonly List<float> _recentDerivativeMagnitudes = new List<float>(); // Rolling window for averaging
        
        /// <summary>
        /// Structure to store accelerometer derivative information
        /// </summary>
        public class AccelerometerDerivative
        {
            public DateTime Timestamp { get; set; }
            public float DerivativeX { get; set; }
            public float DerivativeY { get; set; }
            public float DerivativeZ { get; set; }
            public float Magnitude { get; set; }
        }
        
        private readonly List<AccelerometerDerivative> _accelerometerDerivatives = new List<AccelerometerDerivative>();
        
        private const double BufferDurationSeconds = 3.0;
        private const float LightSensorHighThreshold = 40000.0f; // Updated to 40000 as requested
        private const double ContinuousSaveDurationSeconds = 4.0; // Save for 4 seconds
        private const float AccelerometerDerivativeJumpThreshold = 5.0f; // G/s threshold for detecting jumps (fallback when not enough data)
        private const int DerivativeAveragingWindowSize = 10; // Number of previous derivatives to average
        private const float DerivativeSpikeMultiplier = 1.8f; // Multiplier above average to consider a spike (lower = more sensitive for bowling release)
        private const float MinimumSpikeThreshold = 8.0f; // Minimum absolute threshold (G/s) to avoid false positives from small motions

        /// <summary>
        /// Event fired when sensor data is saved (initial save - triggers file picker)
        /// </summary>
        public event EventHandler<SensorDataSavedEventArgs>? DataSaved;

        /// <summary>
        /// Event fired when an error occurs during saving
        /// </summary>
        public event EventHandler<string>? SaveError;

        /// <summary>
        /// Event fired when continuous save period starts
        /// </summary>
        public event EventHandler? ContinuousSaveStarted;

        /// <summary>
        /// Event fired when continuous save period is complete and data is ready to save
        /// </summary>
        public event EventHandler<SensorDataSavedEventArgs>? ContinuousSaveComplete;

        public SensorBufferManager(IMetaWearService metaWearService)
        {
            _metaWearService = metaWearService ?? throw new ArgumentNullException(nameof(metaWearService));

            // Subscribe to sensor data events
            _metaWearService.AccelerometerDataReceived += OnAccelerometerDataReceived;
            _metaWearService.GyroscopeDataReceived += OnGyroscopeDataReceived;
            _metaWearService.MagnetometerDataReceived += OnMagnetometerDataReceived;
            _metaWearService.LightSensorDataReceived += OnLightSensorDataReceived;

            // Start cleanup timer to remove old data from buffer
            _bufferCleanupTimer = new Timer(CleanupOldBufferData, null, TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(0.5));
        }

        /// <summary>
        /// Starts buffering sensor data
        /// </summary>
        /// <param name="baseFileName">Optional base filename to use when saving (without extension)</param>
        public async Task StartBufferingAsync(string? baseFileName = null)
        {
            if (!_metaWearService.IsConnected)
            {
                // Device not connected, can't buffer
                return;
            }

            lock (_bufferLock)
            {
                _sensorBuffer.Clear();
                _allSavedPoints.Clear();
                _isBuffering = true;
                _hasTriggeredSave = false;
                _isContinuousSaving = false;
                _continuousSaveEndTime = null;
                _baseFileName = baseFileName;
                
                // Reset accelerometer derivative tracking
                _prevAccelX = null;
                _prevAccelY = null;
                _prevAccelZ = null;
                _prevAccelTimestamp = null;
                _accelerometerJumpTimestamps.Clear();
                _accelerometerDerivatives.Clear();
                _recentDerivativeMagnitudes.Clear();
                _hasPrintedFirstJump = false;
            }

            try
            {
                // Start all sensors needed for buffering
                await _metaWearService.StartAccelerometerAsync(50f, 16f);
                await _metaWearService.StartGyroscopeAsync(100f, 2000f);
                await _metaWearService.StartMagnetometerAsync(25f);
                await _metaWearService.StartLightSensorAsync(10f);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error starting sensors for buffering: {ex.Message}");
            }
        }

        /// <summary>
        /// Stops buffering sensor data
        /// </summary>
        public async Task StopBufferingAsync()
        {
            lock (_bufferLock)
            {
                _isBuffering = false;
                _isContinuousSaving = false;
            }

            // Stop continuous save timer
            _continuousSaveTimer?.Dispose();
            _continuousSaveTimer = null;

            try
            {
                // Stop all sensors
                await _metaWearService.StopAccelerometerAsync();
                await _metaWearService.StopGyroscopeAsync();
                await _metaWearService.StopMagnetometerAsync();
                await _metaWearService.StopLightSensorAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error stopping sensors: {ex.Message}");
            }
        }

        /// <summary>
        /// Starts continuous data collection for 4 seconds (data will be saved after collection completes)
        /// </summary>
        public void StartContinuousSave()
        {
            DateTime startTime = DateTime.Now;
            lock (_bufferLock)
            {
                _isContinuousSaving = true;
                _continuousSaveStartTime = startTime;
                _continuousSaveEndTime = startTime.AddSeconds(ContinuousSaveDurationSeconds);
                _allSavedPoints.Clear(); // Clear previous saved points
                
                // Add ALL current buffer points to accumulated list (the full 3 seconds of buffered data)
                // The buffer cleanup timer ensures the buffer only contains the last 3 seconds of data
                var initialPoints = _sensorBuffer
                    .OrderBy(p => p.Timestamp)
                    .ToList();
                
                _allSavedPoints.AddRange(initialPoints);
                System.Diagnostics.Debug.WriteLine($"[SensorBuffer] Starting with {initialPoints.Count} initial points from 3-second buffer (full buffer contents)");
                
                if (initialPoints.Count > 0)
                {
                    var bufferStart = initialPoints.First().Timestamp;
                    var bufferEnd = initialPoints.Last().Timestamp;
                    var bufferDuration = (bufferEnd - bufferStart).TotalSeconds;
                    System.Diagnostics.Debug.WriteLine($"[SensorBuffer] Buffer time range: {bufferStart:HH:mm:ss.fff} to {bufferEnd:HH:mm:ss.fff} (duration: {bufferDuration:F2}s)");
                }
            }

            System.Diagnostics.Debug.WriteLine($"[SensorBuffer] Starting continuous data collection for {ContinuousSaveDurationSeconds} seconds");

            // Fire event to notify that continuous save has started
            ContinuousSaveStarted?.Invoke(this, EventArgs.Empty);

            // Start a timer to accumulate data every 500ms during the 4-second period
            _continuousSaveTimer = new Timer(async _ =>
            {
                if (!_isContinuousSaving || !_isBuffering)
                {
                    return;
                }

                DateTime? endTime;
                lock (_bufferLock)
                {
                    endTime = _continuousSaveEndTime;
                }

                if (endTime.HasValue && DateTime.Now >= endTime.Value)
                {
                    // Time's up, stop continuous data collection
                    lock (_bufferLock)
                    {
                        _isContinuousSaving = false;
                    }
                    _continuousSaveTimer?.Dispose();
                    _continuousSaveTimer = null;

                    // Get all accumulated data
                    List<SensorDataPoint> finalData;
                    lock (_bufferLock)
                    {
                        finalData = _allSavedPoints.OrderBy(p => p.Timestamp).ToList();
                    }
                    
                    System.Diagnostics.Debug.WriteLine($"[SensorBuffer] Continuous data collection completed. Total points: {finalData.Count}");
                    
                    // Save accumulated data to temp file and trigger file picker
                    await SaveAccumulatedDataToTempFileAsync(finalData);
                    return;
                }

                // Just accumulate new data (no file I/O during collection)
                await AccumulateNewDataAsync();
            }, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(500));
        }

        /// <summary>
        /// Accumulates new data points from the sensor buffer
        /// </summary>
        private async Task AccumulateNewDataAsync()
        {
            DateTime? startTime;
            lock (_bufferLock)
            {
                startTime = _continuousSaveStartTime;
            }

            if (!startTime.HasValue)
            {
                return;
            }

            lock (_bufferLock)
            {
                // Get all points from the sensor buffer that are after the start time
                var newPoints = _sensorBuffer
                    .Where(p => p.Timestamp >= startTime.Value)
                    .OrderBy(p => p.Timestamp)
                    .ToList();

                // Add new points to accumulated list (avoid duplicates)
                int addedCount = 0;
                foreach (var point in newPoints)
                {
                    if (!_allSavedPoints.Any(p => Math.Abs((p.Timestamp - point.Timestamp).TotalMilliseconds) < 10))
                    {
                        _allSavedPoints.Add(point);
                        addedCount++;
                    }
                }
                
                if (addedCount > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[SensorBuffer] Added {addedCount} new points (total: {_allSavedPoints.Count})");
                }
            }
        }

        /// <summary>
        /// Saves accumulated data to a temp file and triggers the file picker event
        /// </summary>
        private async Task SaveAccumulatedDataToTempFileAsync(List<SensorDataPoint> data)
        {
            if (data == null || data.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine($"[SensorBuffer] No data to save - accumulated list is empty");
                SaveError?.Invoke(this, "No sensor data collected during the 4-second period");
                return;
            }

            try
            {
                // Get jump timestamps and derivatives
                List<DateTime> jumpTimestamps;
                List<AccelerometerDerivative> derivatives;
                lock (_bufferLock)
                {
                    jumpTimestamps = new List<DateTime>(_accelerometerJumpTimestamps);
                    derivatives = new List<AccelerometerDerivative>(_accelerometerDerivatives);
                }

                // Save to temp directory - user will pick final location via file picker
                string tempFolder = Path.Combine(FileSystem.CacheDirectory, "SensorData");
                Directory.CreateDirectory(tempFolder);

                // Create filename with timestamp
                string fileName = $"sensor_{DateTime.Now:yyyyMMdd_HHmmss}.json";
                string tempFilePath = Path.Combine(tempFolder, fileName);

                // Create a wrapper object that includes sensor data, derivatives, and jump timestamps
                var saveData = new
                {
                    SensorData = data,
                    AccelerometerDerivatives = derivatives,
                    AccelerometerJumpTimestamps = jumpTimestamps,
                    JumpCount = jumpTimestamps.Count,
                    TotalDerivativeCalculations = derivatives.Count
                };

                // Serialize and save to temp location on background thread to avoid blocking UI
                // But ensure the file is fully written before proceeding
                await Task.Run(async () =>
                {
                    var options = new JsonSerializerOptions
                    {
                        WriteIndented = true
                    };

                    string json = JsonSerializer.Serialize(saveData, options);
                    await File.WriteAllTextAsync(tempFilePath, json);
                    
                    // Verify file was written
                    if (!File.Exists(tempFilePath))
                    {
                        throw new IOException($"File was not created at {tempFilePath}");
                    }
                });

                var firstTimestamp = data.First().Timestamp;
                var lastTimestamp = data.Last().Timestamp;
                var duration = (lastTimestamp - firstTimestamp).TotalSeconds;

                System.Diagnostics.Debug.WriteLine($"[SensorBuffer] Saved {data.Count} points to temp file: {tempFilePath}");
                System.Diagnostics.Debug.WriteLine($"[SensorBuffer] Time range: {firstTimestamp:HH:mm:ss.fff} to {lastTimestamp:HH:mm:ss.fff} (duration: {duration:F2}s)");
                System.Diagnostics.Debug.WriteLine($"[SensorBuffer] Accelerometer derivatives: {derivatives.Count} calculations");
                System.Diagnostics.Debug.WriteLine($"[SensorBuffer] Accelerometer jump timestamps: {jumpTimestamps.Count} jumps detected");
                
                // Print derivative values asynchronously to avoid blocking (limit to first 50 for performance)
                _ = Task.Run(() =>
                {
                    System.Diagnostics.Debug.WriteLine($"[SensorBuffer] ========== ACCELEROMETER DERIVATIVES (showing first 50) ==========");
                    int printCount = Math.Min(50, derivatives.Count);
                    for (int i = 0; i < printCount; i++)
                    {
                        var deriv = derivatives[i];
                        System.Diagnostics.Debug.WriteLine($"[SensorBuffer] Derivative #{i + 1} at {deriv.Timestamp:HH:mm:ss.fff}: " +
                            $"dX={deriv.DerivativeX:F3} G/s, dY={deriv.DerivativeY:F3} G/s, dZ={deriv.DerivativeZ:F3} G/s, " +
                            $"Magnitude={deriv.Magnitude:F3} G/s {(deriv.Magnitude >= AccelerometerDerivativeJumpThreshold ? "[JUMP!]" : "")}");
                    }
                    if (derivatives.Count > 50)
                    {
                        System.Diagnostics.Debug.WriteLine($"[SensorBuffer] ... ({derivatives.Count - 50} more derivatives not shown)");
                    }
                    System.Diagnostics.Debug.WriteLine($"[SensorBuffer] ==============================================");
                });

                // Fire event on main thread to trigger file picker
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    ContinuousSaveComplete?.Invoke(this, new SensorDataSavedEventArgs
                    {
                        FilePath = tempFilePath,
                        DataPointCount = data.Count
                    });
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SensorBuffer] Error saving accumulated data to temp file: {ex.Message}");
                SaveError?.Invoke(this, $"Failed to save accumulated data: {ex.Message}");
            }
        }

        /// <summary>
        /// Saves the current buffer to a file
        /// </summary>
        /// <param name="baseFileName">Base filename to use (without extension)</param>
        public async Task SaveBufferAsync(string? baseFileName = null)
        {
            List<SensorDataPoint> bufferCopy;

            lock (_bufferLock)
            {
                // Copy the current buffer (last 3 seconds)
                var cutoffTime = DateTime.Now.AddSeconds(-BufferDurationSeconds);
                bufferCopy = _sensorBuffer
                    .Where(p => p.Timestamp >= cutoffTime)
                    .OrderBy(p => p.Timestamp)
                    .ToList();
                
                System.Diagnostics.Debug.WriteLine($"[SensorBuffer] Copying buffer: {bufferCopy.Count} points from {_sensorBuffer.Count} total points");
            }

            if (bufferCopy.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine("[SensorBuffer] No sensor data to save - buffer is empty");
                SaveError?.Invoke(this, "No sensor data in buffer to save");
                return;
            }

            try
            {
                // Save to temp directory first - user will pick final location via file picker
                string tempFolder = Path.Combine(FileSystem.CacheDirectory, "SensorData");
                Directory.CreateDirectory(tempFolder);
                System.Diagnostics.Debug.WriteLine($"[SensorBuffer] Saving to temp folder: {tempFolder}");

                // Create filename based on provided base filename or timestamp
                string fileName = !string.IsNullOrEmpty(baseFileName)
                    ? $"{baseFileName}_sensor.json"
                    : $"sensor_{DateTime.Now:yyyyMMdd_HHmmss}.json";

                string tempFilePath = Path.Combine(tempFolder, fileName);
                System.Diagnostics.Debug.WriteLine($"[SensorBuffer] Saving to temp file: {tempFilePath}");

                // Serialize and save to temp location
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true
                };

                string json = JsonSerializer.Serialize(bufferCopy, options);
                await File.WriteAllTextAsync(tempFilePath, json);

                // Verify file was created
                if (File.Exists(tempFilePath))
                {
                    var fileInfo = new FileInfo(tempFilePath);
                    System.Diagnostics.Debug.WriteLine($"[SensorBuffer] Sensor data saved to temp file: {tempFilePath} ({bufferCopy.Count} data points, {fileInfo.Length} bytes)");

                    // Fire event on main thread to trigger file picker
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        DataSaved?.Invoke(this, new SensorDataSavedEventArgs
                        {
                            FilePath = tempFilePath,
                            DataPointCount = bufferCopy.Count
                        });
                    });
                }
                else
                {
                    throw new IOException($"File was not created at {tempFilePath}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SensorBuffer] Error saving sensor buffer: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[SensorBuffer] Stack trace: {ex.StackTrace}");
                
                // Fire error event on main thread
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    SaveError?.Invoke(this, ex.Message);
                });
            }
        }


        /// <summary>
        /// Disposes resources
        /// </summary>
        public void Dispose()
        {
            // Unsubscribe from sensor events
            if (_metaWearService != null)
            {
                _metaWearService.AccelerometerDataReceived -= OnAccelerometerDataReceived;
                _metaWearService.GyroscopeDataReceived -= OnGyroscopeDataReceived;
                _metaWearService.MagnetometerDataReceived -= OnMagnetometerDataReceived;
                _metaWearService.LightSensorDataReceived -= OnLightSensorDataReceived;
            }

            // Dispose cleanup timer
            _bufferCleanupTimer?.Dispose();
            
            // Dispose continuous save timer
            _continuousSaveTimer?.Dispose();
            _continuousSaveTimer = null;
        }

        private void OnAccelerometerDataReceived(object? sender, MetaWearAccelerometerData data)
        {
            if (!_isBuffering) return;

            lock (_bufferLock)
            {
                var point = GetOrCreateDataPoint(data.Timestamp);
                point.AccelX = data.X;
                point.AccelY = data.Y;
                point.AccelZ = data.Z;

                // Calculate derivative and detect jumps
                if (_prevAccelX.HasValue && _prevAccelY.HasValue && _prevAccelZ.HasValue && _prevAccelTimestamp.HasValue)
                {
                    // Calculate time difference in seconds
                    double deltaTime = (data.Timestamp - _prevAccelTimestamp.Value).TotalSeconds;
                    
                    if (deltaTime > 0) // Avoid division by zero
                    {
                        // Calculate derivative (rate of change) for each axis (G/s)
                        float derivativeX = (data.X - _prevAccelX.Value) / (float)deltaTime;
                        float derivativeY = (data.Y - _prevAccelY.Value) / (float)deltaTime;
                        float derivativeZ = (data.Z - _prevAccelZ.Value) / (float)deltaTime;

                        // Calculate magnitude of derivative vector
                        float derivativeMagnitude = (float)Math.Sqrt(
                            derivativeX * derivativeX + 
                            derivativeY * derivativeY + 
                            derivativeZ * derivativeZ);

                        // Calculate average of previous derivatives (before adding current)
                        float averageDerivativeMagnitude = 0f;
                        bool hasEnoughData = _recentDerivativeMagnitudes.Count >= DerivativeAveragingWindowSize;
                        
                        if (hasEnoughData)
                        {
                            // Calculate average of previous values only
                            averageDerivativeMagnitude = _recentDerivativeMagnitudes.Average();
                        }

                        // Add current to rolling window for future averaging
                        _recentDerivativeMagnitudes.Add(derivativeMagnitude);
                        
                        // Keep only the most recent values in the window
                        if (_recentDerivativeMagnitudes.Count > DerivativeAveragingWindowSize)
                        {
                            _recentDerivativeMagnitudes.RemoveAt(0);
                        }

                        // Store derivative information (including average)
                        var derivative = new AccelerometerDerivative
                        {
                            Timestamp = data.Timestamp,
                            DerivativeX = derivativeX,
                            DerivativeY = derivativeY,
                            DerivativeZ = derivativeZ,
                            Magnitude = derivativeMagnitude
                        };
                        _accelerometerDerivatives.Add(derivative);

                        // Check if derivative spikes above the average (only if we have enough data points)
                        bool isSpike = false;
                        if (hasEnoughData)
                        {
                            // A spike is when current magnitude is significantly above the average of previous values
                            // For bowling release detection, we use a lower multiplier (1.8x) for better sensitivity
                            float spikeThreshold = Math.Max(
                                averageDerivativeMagnitude * DerivativeSpikeMultiplier,
                                MinimumSpikeThreshold); // Ensure minimum threshold to avoid false positives
                            isSpike = derivativeMagnitude >= spikeThreshold;
                            
                            if (isSpike)
                            {
                                // Save the timestamp when spike is detected
                                _accelerometerJumpTimestamps.Add(data.Timestamp);
                                
                                // Only print the first time a spike is detected
                                if (!_hasPrintedFirstJump)
                                {
                                    _hasPrintedFirstJump = true;
                                    System.Diagnostics.Debug.WriteLine($"[SensorBuffer] FIRST accelerometer derivative spike detected! " +
                                        $"Magnitude: {derivativeMagnitude:F2} G/s (avg: {averageDerivativeMagnitude:F2} G/s, threshold: {spikeThreshold:F2} G/s) " +
                                        $"at {data.Timestamp:HH:mm:ss.fff} " +
                                        $"(dX: {derivativeX:F2}, dY: {derivativeY:F2}, dZ: {derivativeZ:F2} G/s)");
                                }
                                else
                                {
                                    System.Diagnostics.Debug.WriteLine($"[SensorBuffer] Derivative spike detected: " +
                                        $"Magnitude: {derivativeMagnitude:F2} G/s (avg: {averageDerivativeMagnitude:F2} G/s) " +
                                        $"at {data.Timestamp:HH:mm:ss.fff}");
                                }
                            }
                        }
                        else
                        {
                            // Not enough data points yet, use original threshold method as fallback
                            if (derivativeMagnitude >= AccelerometerDerivativeJumpThreshold)
                            {
                                isSpike = true;
                                _accelerometerJumpTimestamps.Add(data.Timestamp);
                                
                                if (!_hasPrintedFirstJump)
                                {
                                    _hasPrintedFirstJump = true;
                                    System.Diagnostics.Debug.WriteLine($"[SensorBuffer] FIRST accelerometer derivative jump detected (insufficient data for averaging)! " +
                                        $"Magnitude: {derivativeMagnitude:F2} G/s at {data.Timestamp:HH:mm:ss.fff} " +
                                        $"(dX: {derivativeX:F2}, dY: {derivativeY:F2}, dZ: {derivativeZ:F2} G/s)");
                                }
                            }
                        }
                    }
                }

                // Update previous values for next calculation
                _prevAccelX = data.X;
                _prevAccelY = data.Y;
                _prevAccelZ = data.Z;
                _prevAccelTimestamp = data.Timestamp;
            }
        }

        private void OnGyroscopeDataReceived(object? sender, MetaWearGyroscopeData data)
        {
            if (!_isBuffering) return;

            lock (_bufferLock)
            {
                var point = GetOrCreateDataPoint(data.Timestamp);
                point.GyroX = data.X;
                point.GyroY = data.Y;
                point.GyroZ = data.Z;
            }
        }

        private void OnMagnetometerDataReceived(object? sender, MetaWearMagnetometerData data)
        {
            if (!_isBuffering) return;

            lock (_bufferLock)
            {
                var point = GetOrCreateDataPoint(data.Timestamp);
                point.MagX = data.X;
                point.MagY = data.Y;
                point.MagZ = data.Z;
            }
        }

        private void OnLightSensorDataReceived(object? sender, MetaWearLightSensorData data)
        {
            if (!_isBuffering) return;

            lock (_bufferLock)
            {
                var point = GetOrCreateDataPoint(data.Timestamp);
                point.Light = data.Visible;

                // Check if light goes high and trigger continuous data collection if not already triggered
                if (!_hasTriggeredSave && data.Visible >= LightSensorHighThreshold)
                {
                    _hasTriggeredSave = true;
                    System.Diagnostics.Debug.WriteLine($"[SensorBuffer] Light sensor went high ({data.Visible}), starting continuous data collection...");
                    System.Diagnostics.Debug.WriteLine($"[SensorBuffer] Current buffer size: {_sensorBuffer.Count} points");
                    
                    // Start continuous data collection directly (no initial file picker)
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        StartContinuousSave();
                    });
                }
            }
        }

        private SensorDataPoint GetOrCreateDataPoint(DateTime timestamp)
        {
            // Try to find a point within 10ms of this timestamp
            var point = _sensorBuffer.FirstOrDefault(p => 
                Math.Abs((p.Timestamp - timestamp).TotalMilliseconds) < 10);

            if (point == null)
            {
                point = new SensorDataPoint { Timestamp = timestamp };
                _sensorBuffer.Add(point);
            }

            return point;
        }

        private void CleanupOldBufferData(object? state)
        {
            if (!_isBuffering) return;

            lock (_bufferLock)
            {
                var cutoffTime = DateTime.Now.AddSeconds(-BufferDurationSeconds);
                _sensorBuffer.RemoveAll(p => p.Timestamp < cutoffTime);
            }
        }
    }

    /// <summary>
    /// Event arguments for sensor data saved event
    /// </summary>
    public class SensorDataSavedEventArgs : EventArgs
    {
        public string FilePath { get; set; } = string.Empty;
        public int DataPointCount { get; set; }
    }
}
