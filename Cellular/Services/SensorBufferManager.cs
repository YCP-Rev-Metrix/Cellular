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
        
        private const double BufferDurationSeconds = 3.0;
        private const float LightSensorHighThreshold = 1000.0f;

        /// <summary>
        /// Event fired when sensor data is saved
        /// </summary>
        public event EventHandler<SensorDataSavedEventArgs>? DataSaved;

        /// <summary>
        /// Event fired when an error occurs during saving
        /// </summary>
        public event EventHandler<string>? SaveError;

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
                _isBuffering = true;
                _hasTriggeredSave = false;
                _baseFileName = baseFileName;
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
            }

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
            }

            if (bufferCopy.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine("No sensor data to save");
                return;
            }

            try
            {
                // Create directory for sensor data
                string sensorDataFolder = Path.Combine(FileSystem.AppDataDirectory, "SensorData");
                Directory.CreateDirectory(sensorDataFolder);

                // Create filename based on provided base filename or timestamp
                string fileName = !string.IsNullOrEmpty(baseFileName)
                    ? $"{baseFileName}_sensor.json"
                    : $"sensor_{DateTime.Now:yyyyMMdd_HHmmss}.json";

                string sensorDataPath = Path.Combine(sensorDataFolder, fileName);

                // Serialize and save
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true
                };

                string json = JsonSerializer.Serialize(bufferCopy, options);
                await File.WriteAllTextAsync(sensorDataPath, json);

                System.Diagnostics.Debug.WriteLine($"Sensor data saved to: {sensorDataPath} ({bufferCopy.Count} data points)");

                // Fire event
                DataSaved?.Invoke(this, new SensorDataSavedEventArgs
                {
                    FilePath = sensorDataPath,
                    DataPointCount = bufferCopy.Count
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving sensor buffer: {ex.Message}");
                SaveError?.Invoke(this, ex.Message);
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

                // Check if light goes high and trigger save if not already triggered
                if (!_hasTriggeredSave && data.Visible >= LightSensorHighThreshold)
                {
                    _hasTriggeredSave = true;
                    // Save buffer asynchronously using stored base filename
                    string? baseFileName = _baseFileName;
                    _ = Task.Run(async () => await SaveBufferAsync(baseFileName));
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
