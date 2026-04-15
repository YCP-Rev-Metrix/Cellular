using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Plugin.BLE;
using Plugin.BLE.Abstractions.Contracts;
using Plugin.BLE.Abstractions.EventArgs;
using Cellular.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.ApplicationModel;
using Cellular.Data;
using Cellular.ViewModel;
using Microsoft.Maui.Storage;
#if ANDROID
using Android;
using Android.Content.PM;
using AndroidX.Core.Content;
using AndroidX.Core.App;
#endif

namespace Cellular
{
    public partial class SmartDot : ContentPage
    {
        private readonly IBluetoothLE _ble = CrossBluetoothLE.Current;
        private readonly IAdapter _adapter = CrossBluetoothLE.Current.Adapter;
        private IMetaWearService _metaWearService; // Not readonly so we can update to singleton if needed
        private readonly UserRepository _userRepository;
        private readonly SmartDotDeviceRepository _deviceRepository;
        private SmartDotDevice? _currentDevice;
        
        private ObservableCollection<BluetoothDevice> _devices;
        private BluetoothDevice? _selectedDevice;
        private bool _isScanning;
        private bool _isConnected;
        private bool _isNavigatingToGraphPage = false;
        private bool _isAutoConnecting = false;
        private bool _hasAttemptedAutoConnect = false;
        
        // Sensor data storage for graphing
        public List<SensorDataPoint> AccelerometerData { get; private set; } = new();
        public List<SensorDataPoint> GyroscopeData { get; private set; } = new();
        public List<SensorDataPoint> MagnetometerData { get; private set; } = new();
        public List<SensorDataPoint> LightSensorData { get; private set; } = new();

        public ObservableCollection<BluetoothDevice> Devices
        {
            get => _devices;
            set
            {
                _devices = value;
                OnPropertyChanged();
            }
        }

        public BluetoothDevice? SelectedDevice
        {
            get => _selectedDevice;
            set
            {
                _selectedDevice = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanConnect)); // Notify CanConnect when selection changes
            }
        }

        public bool IsScanning
        {
            get => _isScanning;
            set
            {
                _isScanning = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanConnect));
            }
        }

        public bool IsConnected
        {
            get => _isConnected;
            set
            {
                _isConnected = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanConnect)); // Notify CanConnect when connection changes
                OnPropertyChanged(nameof(CanDisconnect));
                OnPropertyChanged(nameof(CanStartAccelerometer));
                OnPropertyChanged(nameof(CanStartGyroscope));
                OnPropertyChanged(nameof(CanStartMagnetometer));
                OnPropertyChanged(nameof(CanStartLightSensor));
            }
        }

        public bool CanConnect => SelectedDevice != null && !IsConnected; // Allow connecting while scanning
        public bool CanDisconnect => IsConnected;
        public bool CanStartAccelerometer => IsConnected;
        public bool CanStartGyroscope => IsConnected;
        public bool CanStartMagnetometer => IsConnected;
        public bool CanStartLightSensor => IsConnected;

        public SmartDot()
        {
            InitializeComponent();
            
            // Get MetaWear service from dependency injection
            // Try multiple ways to get the singleton instance
            _metaWearService = Handler?.MauiContext?.Services?.GetService<IMetaWearService>()
                ?? Application.Current?.Handler?.MauiContext?.Services?.GetService<IMetaWearService>()
                ?? Microsoft.Maui.Controls.Application.Current?.Handler?.MauiContext?.Services?.GetService<IMetaWearService>()
                ?? new MetaWearBleService(); // Last resort - but this should not happen with proper DI
            
            // Initialize repositories
            var dbConn = new CellularDatabase().GetConnection();
            _userRepository = new UserRepository(dbConn);
            _deviceRepository = new SmartDotDeviceRepository(dbConn);
            
            Devices = new ObservableCollection<BluetoothDevice>();
            
            // Subscribe to MetaWear service events
            _metaWearService.DeviceDisconnected -= OnDeviceDisconnected; // Remove first to avoid duplicates
            _metaWearService.DeviceDisconnected += OnDeviceDisconnected;
            _metaWearService.DeviceReconnected -= OnDeviceReconnected;
            _metaWearService.DeviceReconnected += OnDeviceReconnected;
            _metaWearService.AccelerometerDataReceived -= OnAccelerometerDataReceived;
            _metaWearService.AccelerometerDataReceived += OnAccelerometerDataReceived;
            _metaWearService.GyroscopeDataReceived -= OnGyroscopeDataReceived;
            _metaWearService.GyroscopeDataReceived += OnGyroscopeDataReceived;
            _metaWearService.MagnetometerDataReceived -= OnMagnetometerDataReceived;
            _metaWearService.MagnetometerDataReceived += OnMagnetometerDataReceived;
            _metaWearService.LightSensorDataReceived -= OnLightSensorDataReceived;
            _metaWearService.LightSensorDataReceived += OnLightSensorDataReceived;
            
            // Handle device selection
            DeviceListView.SelectionChanged += OnDeviceSelected;
            
            // Handle navigation events to detect when returning from graph page
            this.Appearing += OnPageAppearing;
            
            BindingContext = this;
        }

        private async void OnPageAppearing(object? sender, EventArgs e)
        {
            // Reset the flag when page appears (user might have navigated back)
            _isNavigatingToGraphPage = false;

            // Ensure we have the singleton service instance (Handler is available in OnAppearing)
            if (Handler?.MauiContext?.Services != null)
            {
                var serviceFromDI = Handler.MauiContext.Services.GetService<IMetaWearService>();
                if (serviceFromDI != null && serviceFromDI != _metaWearService)
                {
                    // We got a different instance, update our reference to the singleton
                    // Unsubscribe from old instance
                    _metaWearService.DeviceDisconnected -= OnDeviceDisconnected;
                    _metaWearService.DeviceReconnected -= OnDeviceReconnected;
                    _metaWearService.AccelerometerDataReceived -= OnAccelerometerDataReceived;
                    _metaWearService.GyroscopeDataReceived -= OnGyroscopeDataReceived;
                    _metaWearService.MagnetometerDataReceived -= OnMagnetometerDataReceived;
                    _metaWearService.LightSensorDataReceived -= OnLightSensorDataReceived;

                    // Update to singleton
                    _metaWearService = serviceFromDI;

                    // Re-subscribe to new instance
                    _metaWearService.DeviceDisconnected += OnDeviceDisconnected;
                    _metaWearService.DeviceReconnected += OnDeviceReconnected;
                    _metaWearService.AccelerometerDataReceived += OnAccelerometerDataReceived;
                    _metaWearService.GyroscopeDataReceived += OnGyroscopeDataReceived;
                    _metaWearService.MagnetometerDataReceived += OnMagnetometerDataReceived;
                    _metaWearService.LightSensorDataReceived += OnLightSensorDataReceived;
                }
            }
            
            // Re-subscribe to events in case page was recreated
            _metaWearService.DeviceDisconnected -= OnDeviceDisconnected;
            _metaWearService.DeviceDisconnected += OnDeviceDisconnected;
            _metaWearService.DeviceReconnected -= OnDeviceReconnected;
            _metaWearService.DeviceReconnected += OnDeviceReconnected;

            // Update connection status from service (service is singleton, maintains state)
            bool serviceIsConnected = _metaWearService.IsConnected;
            if (serviceIsConnected != IsConnected)
            {
                IsConnected = serviceIsConnected;
                if (serviceIsConnected)
                {
                    StatusLabel.Text = $"Connected to {_metaWearService.MacAddress}";
                }
            }

            // Populate the light sensor threshold entry with the current saved value
            LightThresholdEntry.Text = SensorBufferManager.LightSensorHighThreshold.ToString("0");
            LightThresholdStatusLabel.Text = $"Current threshold: {SensorBufferManager.LightSensorHighThreshold:0}";

            // Populate the record duration entry with the current saved value
            RecordDurationEntry.Text = SensorBufferManager.ContinuousSaveDurationSeconds.ToString("0.##");
            RecordDurationStatusLabel.Text = $"Current duration: {SensorBufferManager.ContinuousSaveDurationSeconds:0.##}s";

            // Populate sensor configuration pickers
            InitSensorConfigPickers();

            // Try to auto-connect to saved SmartDot MAC if user is logged in (only once per page instance)
            if (!_hasAttemptedAutoConnect && !IsConnected)
            {
                _hasAttemptedAutoConnect = true;
                await TryAutoConnectAsync();
            }
        }

        // ── Picker item lists (index → display string / value) ────────────────────
        private static readonly string[] AccelScaleLabels = { "±2 g", "±4 g", "±8 g", "±16 g" };
        private static readonly float[]  AccelScaleValues = { 2f, 4f, 8f, 16f };

        private static readonly string[] AccelFreqLabels = { "0.78 Hz", "1.56 Hz", "3.125 Hz", "6.25 Hz", "12.5 Hz", "25 Hz", "50 Hz", "100 Hz", "200 Hz", "400 Hz", "800 Hz", "1600 Hz" };
        private static readonly float[]  AccelFreqValues = { 0.78f, 1.56f, 3.125f, 6.25f, 12.5f, 25f, 50f, 100f, 200f, 400f, 800f, 1600f };

        private static readonly string[] LightGainLabels = { "1x", "2x", "4x", "8x", "48x", "96x" };
        private static readonly int[]    LightGainValues = { 0, 1, 2, 3, 6, 7 };

        private static readonly string[] LightIntegTimeLabels = { "100 ms", "50 ms", "200 ms", "400 ms", "150 ms", "250 ms", "300 ms", "350 ms" };
        private static readonly int[]    LightIntegTimeValues = { 0, 1, 2, 3, 4, 5, 6, 7 };

        private static readonly string[] LightSamplingRateLabels = { "50 ms", "100 ms", "200 ms", "500 ms", "1000 ms", "2000 ms" };
        private static readonly int[]    LightSamplingRateValues = { 0, 1, 2, 3, 4, 5 };

        private static readonly string[] BaroOversamplingLabels = { "Ultra Low Power", "Low Power", "Standard", "High", "Ultra High" };
        private static readonly string[] BaroAveragingLabels    = { "Off", "2", "4", "8", "16" };
        private static readonly string[] BaroStandbyTimeLabels  = { "0.5 ms", "62.5 ms", "125 ms", "250 ms", "500 ms", "1000 ms", "2000 ms", "4000 ms" };

        private static readonly string[] GyroFreqLabels = { "25 Hz", "50 Hz", "100 Hz", "200 Hz", "400 Hz", "800 Hz", "1600 Hz", "3200 Hz" };
        private static readonly float[]  GyroFreqValues = { 25f, 50f, 100f, 200f, 400f, 800f, 1600f, 3200f };

        private static readonly string[] GyroRangeLabels = { "±125 °/s", "±250 °/s", "±500 °/s", "±1000 °/s", "±2000 °/s" };
        private static readonly float[]  GyroRangeValues = { 125f, 250f, 500f, 1000f, 2000f };
        // ──────────────────────────────────────────────────────────────────────────

        private static int Clamp(int v, int min, int max) => Math.Max(min, Math.Min(max, v));

        private void InitSensorConfigPickers()
        {
            // Accelerometer
            AccelScalePicker.ItemsSource = AccelScaleLabels;
            AccelScalePicker.SelectedIndex = Clamp(ClosestIndex(AccelScaleValues, SensorBufferManager.AccelRange), 0, AccelScaleValues.Length - 1);

            AccelFreqPicker.ItemsSource = AccelFreqLabels;
            AccelFreqPicker.SelectedIndex = ClosestIndex(AccelFreqValues, SensorBufferManager.AccelSampleRate);

            // Ambient Light
            LightGainPicker.ItemsSource = LightGainLabels;
            int gainIdx = Array.IndexOf(LightGainValues, SensorBufferManager.LightGain);
            LightGainPicker.SelectedIndex = Clamp(gainIdx < 0 ? 0 : gainIdx, 0, LightGainValues.Length - 1);

            LightIntegTimePicker.ItemsSource = LightIntegTimeLabels;
            int integIdx = Array.IndexOf(LightIntegTimeValues, SensorBufferManager.LightIntegrationTime);
            LightIntegTimePicker.SelectedIndex = Clamp(integIdx < 0 ? 0 : integIdx, 0, LightIntegTimeValues.Length - 1);

            LightSamplingRatePicker.ItemsSource = LightSamplingRateLabels;
            int rateIdx = Array.IndexOf(LightSamplingRateValues, SensorBufferManager.LightMeasurementRate);
            LightSamplingRatePicker.SelectedIndex = Clamp(rateIdx < 0 ? 1 : rateIdx, 0, LightSamplingRateValues.Length - 1);

            // Barometer
            BaroOversamplingPicker.ItemsSource = BaroOversamplingLabels;
            BaroOversamplingPicker.SelectedIndex = Clamp(SensorBufferManager.BaroOversampling, 0, BaroOversamplingLabels.Length - 1);

            BaroAveragingPicker.ItemsSource = BaroAveragingLabels;
            BaroAveragingPicker.SelectedIndex = Clamp(SensorBufferManager.BaroIirFilter, 0, BaroAveragingLabels.Length - 1);

            BaroStandbyTimePicker.ItemsSource = BaroStandbyTimeLabels;
            BaroStandbyTimePicker.SelectedIndex = Clamp(SensorBufferManager.BaroStandbyTime, 0, BaroStandbyTimeLabels.Length - 1);

            // Gyroscope
            GyroFreqPicker.ItemsSource = GyroFreqLabels;
            GyroFreqPicker.SelectedIndex = ClosestIndex(GyroFreqValues, SensorBufferManager.GyroSampleRate);

            GyroRangePicker.ItemsSource = GyroRangeLabels;
            GyroRangePicker.SelectedIndex = ClosestIndex(GyroRangeValues, SensorBufferManager.GyroRange);

            // Magnetometer — display current frequency read-only
            MagFrequencyLabel.Text = $"Frequency: {SensorBufferManager.MagSampleRate:0} Hz";
        }

        private static int ClosestIndex(float[] values, float target)
        {
            int best = 0;
            float bestDist = Math.Abs(values[0] - target);
            for (int i = 1; i < values.Length; i++)
            {
                float dist = Math.Abs(values[i] - target);
                if (dist < bestDist) { bestDist = dist; best = i; }
            }
            return best;
        }

        private async void OnResetDeviceClicked(object sender, EventArgs e)
        {
            bool confirm = await DisplayAlert("Reset Device",
                "This will reset the SmartDot device. Are you sure?", "Reset", "Cancel");
            if (!confirm) return;

            try
            {
                await _metaWearService.ResetAsync();
                await DisplayAlert("Reset", "Device reset command sent.", "OK");
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Failed to reset device: {ex.Message}", "OK");
            }
        }

        private async void OnSleepModeClicked(object sender, EventArgs e)
        {
            bool confirm = await DisplayAlert("Sleep Mode",
                "Put the SmartDot into deep sleep? It will disconnect and stop advertising to save battery.", "Sleep", "Cancel");
            if (!confirm) return;

            try
            {
                await _metaWearService.SleepAsync();

                // Give the device a moment to process the sleep command, then disconnect
                await Task.Delay(500);
                await _metaWearService.StopAccelerometerAsync();
                await _metaWearService.StopGyroscopeAsync();
                await _metaWearService.StopMagnetometerAsync();
                await _metaWearService.StopLightSensorAsync();
                await _metaWearService.DisconnectAsync();

                IsConnected = false;
                StatusLabel.Text = "SmartDot is sleeping";
                ClearDeviceInfo();
                AccelerometerLabel.Text = "";
                GyroscopeLabel.Text = "";
                MagnetometerLabel.Text = "";
                LightSensorLabel.Text = "";

                await UpdateIsConnectedStatusAsync(false);
                await DisplayAlert("Sleep", "SmartDot is now in deep sleep mode.", "OK");
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Failed to send sleep command: {ex.Message}", "OK");
            }
        }

        private async void OnSaveSensorConfigClicked(object sender, EventArgs e)
        {
            // Accelerometer
            if (AccelScalePicker.SelectedIndex >= 0)
                SensorBufferManager.AccelRange = AccelScaleValues[AccelScalePicker.SelectedIndex];
            if (AccelFreqPicker.SelectedIndex >= 0)
                SensorBufferManager.AccelSampleRate = AccelFreqValues[AccelFreqPicker.SelectedIndex];

            // Ambient Light
            if (LightGainPicker.SelectedIndex >= 0)
                SensorBufferManager.LightGain = LightGainValues[LightGainPicker.SelectedIndex];
            if (LightIntegTimePicker.SelectedIndex >= 0)
                SensorBufferManager.LightIntegrationTime = LightIntegTimeValues[LightIntegTimePicker.SelectedIndex];
            if (LightSamplingRatePicker.SelectedIndex >= 0)
                SensorBufferManager.LightMeasurementRate = LightSamplingRateValues[LightSamplingRatePicker.SelectedIndex];

            // Barometer
            if (BaroOversamplingPicker.SelectedIndex >= 0)
                SensorBufferManager.BaroOversampling = BaroOversamplingPicker.SelectedIndex;
            if (BaroAveragingPicker.SelectedIndex >= 0)
                SensorBufferManager.BaroIirFilter = BaroAveragingPicker.SelectedIndex;
            if (BaroStandbyTimePicker.SelectedIndex >= 0)
                SensorBufferManager.BaroStandbyTime = BaroStandbyTimePicker.SelectedIndex;

            // Gyroscope
            if (GyroFreqPicker.SelectedIndex >= 0)
                SensorBufferManager.GyroSampleRate = GyroFreqValues[GyroFreqPicker.SelectedIndex];
            if (GyroRangePicker.SelectedIndex >= 0)
                SensorBufferManager.GyroRange = GyroRangeValues[GyroRangePicker.SelectedIndex];

            await SaveCurrentDeviceSettingsAsync();
            await DisplayAlert("Saved", "Sensor configuration saved. Changes take effect on the next recording.", "OK");
        }

        private async void OnSaveLightThresholdClicked(object sender, EventArgs e)
        {
            if (float.TryParse(LightThresholdEntry.Text, out float newThreshold) && newThreshold > 0)
            {
                SensorBufferManager.LightSensorHighThreshold = newThreshold;
                LightThresholdStatusLabel.Text = $"Saved! Current threshold: {newThreshold:0}";
                LightThresholdStatusLabel.TextColor = Colors.Green;
                await SaveCurrentDeviceSettingsAsync();
            }
            else
            {
                LightThresholdStatusLabel.Text = "Invalid value. Please enter a positive number.";
                LightThresholdStatusLabel.TextColor = Colors.Red;
            }
        }

        private async void OnSaveRecordDurationClicked(object sender, EventArgs e)
        {
            if (double.TryParse(RecordDurationEntry.Text, out double newDuration) && newDuration > 0)
            {
                SensorBufferManager.ContinuousSaveDurationSeconds = newDuration;
                RecordDurationStatusLabel.Text = $"Saved! Current duration: {newDuration:0.##}s";
                RecordDurationStatusLabel.TextColor = Colors.Green;
                await SaveCurrentDeviceSettingsAsync();
            }
            else
            {
                RecordDurationStatusLabel.Text = "Invalid value. Please enter a positive number.";
                RecordDurationStatusLabel.TextColor = Colors.Red;
            }
        }

        private async Task TryAutoConnectAsync()
        {
            // First, check if already connected to the service
            bool serviceIsConnected = _metaWearService.IsConnected;
            if (serviceIsConnected)
            {
                // Already connected - update UI state
                IsConnected = true;
                StatusLabel.Text = $"Already connected to {_metaWearService.MacAddress}";

                // Update IsConnected status in database
                await UpdateIsConnectedStatusAsync(true);

                // Load or create device profile
                if (!string.IsNullOrEmpty(_metaWearService.MacAddress))
                    await LoadOrCreateDeviceProfileAsync(_metaWearService.MacAddress);

                // Try to get device info
                try
                {
                    var deviceInfo = await _metaWearService.GetDeviceInfoAsync();
                    ShowDeviceInfo(deviceInfo);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error getting device info: {ex.Message}");
                    ShowDeviceInfo(new Services.DeviceInfo());
                }
                return;
            }
            
            // Check if user is logged in
            int userId = Preferences.Get("UserId", -1);
            if (userId == -1 || _isAutoConnecting || IsConnected)
            {
                return; // No user logged in, already connecting, or already connected
            }

            try
            {
                // Get saved SmartDot MAC address
                string? savedMac = await _userRepository.GetSmartDotMacAsync(userId);
                
                if (string.IsNullOrEmpty(savedMac))
                {
                    return; // No saved MAC address
                }

                _isAutoConnecting = true;
                StatusLabel.Text = "Auto-connecting to saved device...";

                // Check if Bluetooth is on
                if (!_ble.IsOn)
                {
                    StatusLabel.Text = "Bluetooth is off - cannot auto-connect";
                    _isAutoConnecting = false;
                    return;
                }

                // Start scanning to find the device
                Devices.Clear();
                _adapter.DeviceDiscovered += OnDeviceDiscoveredForAutoConnect;
                
                // Scan for a short time to find the device
                await _adapter.StartScanningForDevicesAsync();
                
                // Wait a bit for device discovery
                await Task.Delay(3000);
                
                // Stop scanning
                _adapter.DeviceDiscovered -= OnDeviceDiscoveredForAutoConnect;
                await _adapter.StopScanningForDevicesAsync();

                // Find the device with matching MAC address
                var targetDevice = Devices.FirstOrDefault(d => 
                    d.Id.Equals(savedMac, StringComparison.OrdinalIgnoreCase) || 
                    d.Id.Replace(":", "").Equals(savedMac.Replace(":", ""), StringComparison.OrdinalIgnoreCase));

                if (targetDevice?.Device != null)
                {
                    // Found the device, connect to it
                    SelectedDevice = targetDevice;
                    bool connected = await _metaWearService.ConnectAsync(targetDevice.Device);
                    
                    if (connected)
                    {
                        IsConnected = true;
                        StatusLabel.Text = $"Auto-connected to {targetDevice.Name}";

                        // Update IsConnected status in database
                        await UpdateIsConnectedStatusAsync(true);

                        // Load or create device profile
                        await LoadOrCreateDeviceProfileAsync(targetDevice.Id);

                        // Get device info
                        try
                        {
                            var deviceInfo = await _metaWearService.GetDeviceInfoAsync();
                            ShowDeviceInfo(deviceInfo);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error getting device info: {ex.Message}");
                            ClearDeviceInfo();
                        }
                    }
                    else
                    {
                        StatusLabel.Text = "Auto-connect failed - device found but connection failed";
                    }
                }
                else
                {
                    StatusLabel.Text = "Saved device not found - please scan and connect manually";
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error during auto-connect: {ex.Message}");
                StatusLabel.Text = "Auto-connect failed";
            }
            finally
            {
                _isAutoConnecting = false;
            }
        }

        private void OnDeviceDiscoveredForAutoConnect(object? sender, DeviceEventArgs e)
        {
            if (e.Device == null)
                return;

            // Filter for MetaWear devices (MMS and MMC)
            if (string.IsNullOrEmpty(e.Device.Name) ||
                (!e.Device.Name.Contains("MetaWear", StringComparison.OrdinalIgnoreCase) &&
                 !e.Device.Name.Contains("MMS", StringComparison.OrdinalIgnoreCase) &&
                 !e.Device.Name.Contains("MMC", StringComparison.OrdinalIgnoreCase) &&
                 !e.Device.Name.Contains("MetaMotion", StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            string deviceId = e.Device.Id.ToString();
            MainThread.BeginInvokeOnMainThread(() =>
            {
                var existingDevice = Devices.FirstOrDefault(d => d.Id == deviceId);
                if (existingDevice == null)
                {
                    var entry = new BluetoothDevice
                    {
                        Id = deviceId,
                        Name = e.Device.Name ?? "Unknown",
                        Rssi = e.Device.Rssi,
                        Device = e.Device
                    };
                    Devices.Add(entry);
                    _ = Task.Run(async () =>
                    {
                        var saved = await _deviceRepository.GetByMacAsync(deviceId);
                        if (saved != null && !string.IsNullOrEmpty(saved.DeviceName))
                            MainThread.BeginInvokeOnMainThread(() => entry.SavedName = saved.DeviceName);
                    });
                }
                else
                {
                    existingDevice.Rssi = e.Device.Rssi;
                    existingDevice.Device = e.Device;
                }
            });
        }

        private void OnDeviceSelected(object? sender, SelectionChangedEventArgs e)
        {
            if (DeviceListView.SelectedItem is BluetoothDevice device)
            {
                SelectedDevice = device;
            }
        }

        private void OnDeviceDisconnected(object? sender, string macAddress)
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                IsConnected = false;
                StatusLabel.Text = "Disconnected (reconnect failed)";
                await UpdateIsConnectedStatusAsync(false);
            });
        }

        private void OnDeviceReconnected(object? sender, string macAddress)
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                IsConnected = true;
                StatusLabel.Text = $"Reconnected to {macAddress}";
                await UpdateIsConnectedStatusAsync(true);
            });
        }

        private DateTime _lastAccelerometerUpdate = DateTime.MinValue;
        private DateTime _lastGyroscopeUpdate = DateTime.MinValue;
        private const int ACCELEROMETER_UPDATE_THROTTLE_MS = 50; // Update UI max 20 times per second (50ms = 20Hz) to match gyroscope
        private const int GYROSCOPE_UPDATE_THROTTLE_MS = 50; // Update UI max 20 times per second (50ms = 20Hz)

        private void OnAccelerometerDataReceived(object? sender, Services.MetaWearAccelerometerData data)
        {
            // Store data for graphing (store all data, not throttled)
            var timestamp = DateTime.Now;
            lock (AccelerometerData)
            {
                AccelerometerData.Add(new SensorDataPoint
                {
                    Timestamp = timestamp,
                    X = data.X,
                    Y = data.Y,
                    Z = data.Z
                });
                
                // Keep only last 1000 points to avoid memory issues
                if (AccelerometerData.Count > 1000)
                {
                    AccelerometerData.RemoveAt(0);
                }
                
                // Debug: log every 100th data point (only if debug logging is enabled)
                if (Services.MetaWearBleService.IsDebugLoggingEnabled && AccelerometerData.Count % 100 == 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[Bluetooth] Accelerometer data count: {AccelerometerData.Count}");
                }
            }
            
            // Throttle UI updates - match gyroscope throttle (50ms = 20Hz) for consistent visual speed
            var now = DateTime.Now;
            if ((now - _lastAccelerometerUpdate).TotalMilliseconds < ACCELEROMETER_UPDATE_THROTTLE_MS)
            {
                return; // Skip this update to avoid overwhelming the UI thread
            }
            
            _lastAccelerometerUpdate = now;
            
            MainThread.BeginInvokeOnMainThread(() =>
            {
                AccelerometerLabel.Text = $"Accel: X={data.X:F2}, Y={data.Y:F2}, Z={data.Z:F2}";
            });
        }

        private void OnGyroscopeDataReceived(object? sender, Services.MetaWearGyroscopeData data)
        {
            // Store data for graphing (store all data, not throttled)
            var timestamp = DateTime.Now;
            lock (GyroscopeData)
            {
                GyroscopeData.Add(new SensorDataPoint
                {
                    Timestamp = timestamp,
                    X = data.X,
                    Y = data.Y,
                    Z = data.Z
                });
                
                // Keep only last 1000 points to avoid memory issues
                if (GyroscopeData.Count > 1000)
                {
                    GyroscopeData.RemoveAt(0);
                }
                
                // Debug: log every 100th data point (only if debug logging is enabled)
                if (Services.MetaWearBleService.IsDebugLoggingEnabled && GyroscopeData.Count % 100 == 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[Bluetooth] Gyroscope data count: {GyroscopeData.Count}");
                }
            }
            
            // Throttle UI updates to avoid lag - only update every 50ms (20Hz)
            var now = DateTime.Now;
            if ((now - _lastGyroscopeUpdate).TotalMilliseconds < GYROSCOPE_UPDATE_THROTTLE_MS)
            {
                return; // Skip this update to avoid overwhelming the UI thread
            }
            
            _lastGyroscopeUpdate = now;
            
            MainThread.BeginInvokeOnMainThread(() =>
            {
                GyroscopeLabel.Text = $"Gyro: X={data.X:F2}, Y={data.Y:F2}, Z={data.Z:F2}";
            });
        }

        private DateTime _lastMagnetometerUpdate = DateTime.MinValue;
        private DateTime _lastLightSensorUpdate = DateTime.MinValue;
        private const int MAGNETOMETER_UPDATE_THROTTLE_MS = 50; // Update UI max 20 times per second
        private const int LIGHT_SENSOR_UPDATE_THROTTLE_MS = 100; // Update UI max 10 times per second

        private void OnMagnetometerDataReceived(object? sender, Services.MetaWearMagnetometerData data)
        {
            // Store data for graphing (store all data, not throttled)
            var timestamp = DateTime.Now;
            MagnetometerData.Add(new SensorDataPoint
            {
                Timestamp = timestamp,
                X = data.X,
                Y = data.Y,
                Z = data.Z
            });
            
            // Keep only last 1000 points to avoid memory issues
            if (MagnetometerData.Count > 1000)
            {
                MagnetometerData.RemoveAt(0);
            }
            
            // Throttle UI updates to avoid lag
            var now = DateTime.Now;
            if ((now - _lastMagnetometerUpdate).TotalMilliseconds < MAGNETOMETER_UPDATE_THROTTLE_MS)
            {
                return;
            }
            
            _lastMagnetometerUpdate = now;
            
            MainThread.BeginInvokeOnMainThread(() =>
            {
                MagnetometerLabel.Text = $"Mag: X={data.X:F2}µT, Y={data.Y:F2}µT, Z={data.Z:F2}µT";
            });
        }

        private void OnLightSensorDataReceived(object? sender, Services.MetaWearLightSensorData data)
        {
            // Store data for graphing (store all data, not throttled)
            var timestamp = DateTime.Now;
            LightSensorData.Add(new SensorDataPoint
            {
                Timestamp = timestamp,
                X = data.Visible,
                Y = 0,
                Z = 0 // Light sensor only has visible value
            });
            
            // Keep only last 1000 points to avoid memory issues
            if (LightSensorData.Count > 1000)
            {
                LightSensorData.RemoveAt(0);
            }
            
            // Throttle UI updates to avoid lag
            var now = DateTime.Now;
            if ((now - _lastLightSensorUpdate).TotalMilliseconds < LIGHT_SENSOR_UPDATE_THROTTLE_MS)
            {
                return;
            }
            
            _lastLightSensorUpdate = now;
            
            MainThread.BeginInvokeOnMainThread(() =>
            {
                LightSensorLabel.Text = $"Light: Visible={data.Visible}";
            });
        }

        private async void OnScanClicked(object sender, EventArgs e)
        {
            if (IsScanning)
            {
                await StopScanningAsync();
            }
            else
            {
                await StartScanningAsync();
            }
        }

        private async Task StartScanningAsync()
        {
            // Check Bluetooth state (cross-platform)
            if (!_ble.IsOn)
            {
                await DisplayAlertAsync("Bluetooth", "Please enable Bluetooth", "OK");
                return;
            }

            // Request permissions for Android (required for BLE scanning)
            // This is going to be a bitch for the rest of the versiosn
#if ANDROID
            try
            {
                var androidVersion = (int)Android.OS.Build.VERSION.SdkInt;
                
                if (androidVersion >= 31) // Android 12+ (API 31+)
                {
                    // Request BLUETOOTH_SCAN and BLUETOOTH_CONNECT permissions for Android 12+
                    var activity = Platform.CurrentActivity ?? throw new InvalidOperationException("Android activity not available");
                    
                    var bluetoothScanPermission = Manifest.Permission.BluetoothScan;
                    var bluetoothConnectPermission = Manifest.Permission.BluetoothConnect;
                    
                    var scanGranted = ContextCompat.CheckSelfPermission(activity, bluetoothScanPermission) == Permission.Granted;
                    var connectGranted = ContextCompat.CheckSelfPermission(activity, bluetoothConnectPermission) == Permission.Granted;
                    
                    if (!scanGranted || !connectGranted)
                    {
                        var permissions = new List<string>();
                        if (!scanGranted) permissions.Add(bluetoothScanPermission);
                        if (!connectGranted) permissions.Add(bluetoothConnectPermission);
                        
                        // Request permissions and wait for result
                        try
                        {
                            var grantResults = await MainActivity.RequestPermissionsAsync(
                                permissions.ToArray(), 
                                MainActivity.BluetoothPermissionRequestCode);
                            
                            // Check if all permissions were granted
                            var allGranted = grantResults.Length > 0 && 
                                grantResults.All(r => r == Permission.Granted);
                            
                            if (!allGranted)
                            {
                                // Re-check permissions to be sure
                                scanGranted = ContextCompat.CheckSelfPermission(activity, bluetoothScanPermission) == Permission.Granted;
                                connectGranted = ContextCompat.CheckSelfPermission(activity, bluetoothConnectPermission) == Permission.Granted;
                                
                                if (!scanGranted || !connectGranted)
                                {
                                    await DisplayAlertAsync("Permission Required", 
                                        "Bluetooth permission is required to scan for Bluetooth devices. Please grant BLUETOOTH_SCAN and BLUETOOTH_CONNECT permissions in your device settings.\n\nYou can do this by going to:\nSettings > Apps > Cellular > Permissions", 
                                        "OK");
                                    return;
                                }
                            }
                        }
                        catch (Exception permEx)
                        {
                            System.Diagnostics.Debug.WriteLine($"Permission request exception: {permEx.Message}");
                            await DisplayAlertAsync("Permission Error", 
                                "Unable to request Bluetooth permissions. Please grant BLUETOOTH_SCAN and BLUETOOTH_CONNECT permissions manually in your device settings.", 
                                "OK");
                            return;
                        }
                    }
                }
                else
                {
                    // For Android 6.0-11: Request location permission (required for BLE scanning)
                    var locationStatus = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
                    if (locationStatus != PermissionStatus.Granted)
                    {
                        await DisplayAlert("Permission Required", 
                            "Location permission is required to scan for Bluetooth devices. Please grant the permission in your device settings.", 
                            "OK");
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Permission request error: {ex.Message}");
                await DisplayAlert("Permission Error", 
                    $"Unable to request permissions: {ex.Message}. Please ensure Bluetooth permissions are granted in device settings.", 
                    "OK");
                return;
            }
#endif

            Devices.Clear();
            IsScanning = true;
            ScanButton.Text = "Stop Scan";
            StatusLabel.Text = "Scanning...";

            _adapter.DeviceDiscovered += OnDeviceDiscovered;
            _adapter.ScanTimeoutElapsed += OnScanTimeout;

            try
            {
                // Scan for BLE devices (cross-platform)
                // Plugin.BLE handles platform differences automatically
                // Note: ScanTimeoutElapsed may not fire on all platforms, so we handle timeout manually
                await _adapter.StartScanningForDevicesAsync();
                
                // Set a manual timeout as fallback (cross-platform)
                _ = Task.Delay(TimeSpan.FromSeconds(10)).ContinueWith(_ =>
                {
                    if (IsScanning)
                    {
                        MainThread.BeginInvokeOnMainThread(async () =>
                        {
                            await StopScanningAsync();
                        });
                    }
                });
            }
            catch (Exception ex)
            {
                await DisplayAlertAsync("Scan Error", ex.Message, "OK");
                IsScanning = false;
                ScanButton.Text = "Scan";
                StatusLabel.Text = "Scan failed";
            }
        }

        private async Task StopScanningAsync()
        {
            try
            {
                // Unsubscribe from events (cross-platform)
                _adapter.DeviceDiscovered -= OnDeviceDiscovered;
                _adapter.ScanTimeoutElapsed -= OnScanTimeout;
                
                // Stop scanning (cross-platform)
                // Plugin.BLE handles platform differences automatically
                await _adapter.StopScanningForDevicesAsync();
                
                IsScanning = false;
                ScanButton.Text = "Scan";
                StatusLabel.Text = $"Found {Devices.Count} device(s)";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error stopping scan: {ex.Message}");
                IsScanning = false;
                ScanButton.Text = "Scan";
                StatusLabel.Text = "Scan stopped";
            }
        }

        private void OnDeviceDiscovered(object? sender, DeviceEventArgs e)
        {
            if (e.Device == null)
                return;

            // Filter for MetaWear devices (MMS and MMC)
            if (string.IsNullOrEmpty(e.Device.Name) ||
                (!e.Device.Name.Contains("MetaWear", StringComparison.OrdinalIgnoreCase) &&
                 !e.Device.Name.Contains("MMS", StringComparison.OrdinalIgnoreCase) &&
                 !e.Device.Name.Contains("MMC", StringComparison.OrdinalIgnoreCase) &&
                 !e.Device.Name.Contains("MetaMotion", StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            string deviceId = e.Device.Id.ToString();
            MainThread.BeginInvokeOnMainThread(() =>
            {
                var existingDevice = Devices.FirstOrDefault(d => d.Id == deviceId);
                if (existingDevice == null)
                {
                    var entry = new BluetoothDevice
                    {
                        Id = deviceId,
                        Name = e.Device.Name ?? "Unknown",
                        Rssi = e.Device.Rssi,
                        Device = e.Device
                    };
                    Devices.Add(entry);
                    // Async look-up: replace name with saved friendly name if one exists
                    _ = Task.Run(async () =>
                    {
                        var saved = await _deviceRepository.GetByMacAsync(deviceId);
                        if (saved != null && !string.IsNullOrEmpty(saved.DeviceName))
                            MainThread.BeginInvokeOnMainThread(() => entry.SavedName = saved.DeviceName);
                    });
                }
                else
                {
                    existingDevice.Rssi = e.Device.Rssi;
                    existingDevice.Device = e.Device;
                }
            });
        }

        private void OnScanTimeout(object? sender, EventArgs e)
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await StopScanningAsync();
            });
        }

        private async void OnConnectClicked(object sender, EventArgs e)
        {
            if (SelectedDevice == null)
            {
                await DisplayAlertAsync("Connect", "Please select a device first", "OK");
                return;
            }

            try
            {
                if (SelectedDevice.Device == null)
                {
                    await DisplayAlertAsync("Connect", "Device object is not available. Please scan for devices again.", "OK");
                    StatusLabel.Text = "Device not available";
                    return;
                }

                StatusLabel.Text = "Connecting...";
                
                // Connect using the IDevice object (cross-platform)
                bool connected = await _metaWearService.ConnectAsync(SelectedDevice.Device);
                
                if (connected)
                {
                    // Stop scanning if still in progress
                    if (IsScanning)
                    {
                        await StopScanningAsync();
                    }

                    IsConnected = true;
                    StatusLabel.Text = $"Connected to {SelectedDevice.Name}";

                    // Save the MAC address to user's profile
                    await SaveSmartDotMacAsync(SelectedDevice.Id);

                    // Update IsConnected status in database
                    await UpdateIsConnectedStatusAsync(true);

                    // Load or create device profile (prompts for name if new device)
                    await LoadOrCreateDeviceProfileAsync(SelectedDevice.Id);

                    // Get device info
                    try
                    {
                        var deviceInfo = await _metaWearService.GetDeviceInfoAsync();
                        ShowDeviceInfo(deviceInfo);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error getting device info: {ex.Message}");
                        ShowDeviceInfo(new Services.DeviceInfo());
                    }
                }
                else
                {
                    var errorMessage = $"Failed to connect to {SelectedDevice.Name}.\n\n" +
                        "Possible issues:\n" +
                        "• MetaWear service or characteristics not found\n" +
                        "• Device may be in use by another app\n" +
                        "• Check Debug output for detailed error messages";
                    await DisplayAlertAsync("Connection Failed", errorMessage, "OK");
                    StatusLabel.Text = "Connection failed - check Debug logs";
                }
            }
            catch (Exception ex)
            {
                await DisplayAlertAsync("Connect Error", ex.Message, "OK");
                StatusLabel.Text = "Connection error";
            }
        }

        private async void OnDisconnectClicked(object sender, EventArgs e)
        {
            try
            {
                await _metaWearService.StopAccelerometerAsync();
                await _metaWearService.StopGyroscopeAsync();
                await _metaWearService.StopMagnetometerAsync();
                await _metaWearService.StopLightSensorAsync();
                await _metaWearService.DisconnectAsync();
                
                IsConnected = false;
                StatusLabel.Text = "Disconnected";
                ClearDeviceInfo();
                AccelerometerLabel.Text = "";
                GyroscopeLabel.Text = "";
                MagnetometerLabel.Text = "";
                LightSensorLabel.Text = "";
                
                // Update IsConnected status in database
                await UpdateIsConnectedStatusAsync(false);
            }
            catch (Exception ex)
            {
                await DisplayAlertAsync("Disconnect Error", ex.Message, "OK");
            }
        }

        private async void OnStartAccelerometerClicked(object sender, EventArgs e)
        {
            try
            {
                await _metaWearService.StartAccelerometerAsync(100f, 16f); // Increased to 100Hz to match gyroscope
                StatusLabel.Text = "Accelerometer started";
            }
            catch (Exception ex)
            {
                await DisplayAlertAsync("Error", ex.Message, "OK");
            }
        }

        private async void OnStopAccelerometerClicked(object sender, EventArgs e)
        {
            try
            {
                await _metaWearService.StopAccelerometerAsync();
                AccelerometerLabel.Text = "";
                StatusLabel.Text = "Accelerometer stopped";
            }
            catch (Exception ex)
            {
                await DisplayAlertAsync("Error", ex.Message, "OK");
            }
        }

        private async void OnStartGyroscopeClicked(object sender, EventArgs e)
        {
            try
            {
                await _metaWearService.StartGyroscopeAsync(100f, 2000f);
                StatusLabel.Text = "Gyroscope started";
            }
            catch (Exception ex)
            {
                await DisplayAlertAsync("Error", ex.Message, "OK");
            }
        }

        private async void OnStopGyroscopeClicked(object sender, EventArgs e)
        {
            try
            {
                await _metaWearService.StopGyroscopeAsync();
                GyroscopeLabel.Text = "";
                StatusLabel.Text = "Gyroscope stopped";
            }
            catch (Exception ex)
            {
                await DisplayAlertAsync("Error", ex.Message, "OK");
            }
        }

        private async void OnStartMagnetometerClicked(object sender, EventArgs e)
        {
            try
            {
                await _metaWearService.StartMagnetometerAsync(25f);
                StatusLabel.Text = "Magnetometer started";
            }
            catch (Exception ex)
            {
                await DisplayAlertAsync("Error", ex.Message, "OK");
            }
        }

        private async void OnStopMagnetometerClicked(object sender, EventArgs e)
        {
            try
            {
                await _metaWearService.StopMagnetometerAsync();
                MagnetometerLabel.Text = "";
                StatusLabel.Text = "Magnetometer stopped";
            }
            catch (Exception ex)
            {
                await DisplayAlertAsync("Error", ex.Message, "OK");
            }
        }

        private async void OnStartLightSensorClicked(object sender, EventArgs e)
        {
            try
            {
                await _metaWearService.StartLightSensorAsync(10f);
                StatusLabel.Text = "Light sensor started";
            }
            catch (Exception ex)
            {
                await DisplayAlertAsync("Error", ex.Message, "OK");
            }
        }

        private async void OnStopLightSensorClicked(object sender, EventArgs e)
        {
            try
            {
                await _metaWearService.StopLightSensorAsync();
                LightSensorLabel.Text = "";
                StatusLabel.Text = "Light sensor stopped";
            }
            catch (Exception ex)
            {
                await DisplayAlertAsync("Error", ex.Message, "OK");
            }
        }

        private async void OnProbeClicked(object sender, EventArgs e)
        {
            try
            {
                StatusLabel.Text = "Probing device...";
                await _metaWearService.ProbeDeviceAsync();
                StatusLabel.Text = "Probe complete - check debug logs";
            }
            catch (Exception ex)
            {
                await DisplayAlertAsync("Probe Error", ex.Message, "OK");
                StatusLabel.Text = "Probe failed";
            }
        }

        private async void OnShowGraphClicked(object sender, EventArgs e)
        {
            // Set flag to prevent disconnection when navigating to graph page
            _isNavigatingToGraphPage = true;
            
            List<SensorDataPoint> accelData, gyroData, magData, lightData;
            
            lock (AccelerometerData)
            {
                accelData = AccelerometerData.ToList();
            }
            lock (GyroscopeData)
            {
                gyroData = GyroscopeData.ToList();
            }
            lock (MagnetometerData)
            {
                magData = MagnetometerData.ToList();
            }
            lock (LightSensorData)
            {
                lightData = LightSensorData.ToList();
            }
            
            // Only log if debug logging is enabled
            if (Services.MetaWearBleService.IsDebugLoggingEnabled)
            {
                System.Diagnostics.Debug.WriteLine($"[Graph] Data counts - Accel: {accelData.Count}, Gyro: {gyroData.Count}, Mag: {magData.Count}, Light: {lightData.Count}");
            }
            
            var graphPage = new SensorGraphPage(
                accelData,
                gyroData,
                magData,
                lightData);
            await Navigation.PushAsync(graphPage);
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();

            // Unsubscribe from events when leaving the page (but stay connected)
            if (!_isNavigatingToGraphPage)
            {
                _metaWearService.DeviceDisconnected -= OnDeviceDisconnected;
                _metaWearService.DeviceReconnected -= OnDeviceReconnected;
                _metaWearService.AccelerometerDataReceived -= OnAccelerometerDataReceived;
                _metaWearService.GyroscopeDataReceived -= OnGyroscopeDataReceived;
                _metaWearService.MagnetometerDataReceived -= OnMagnetometerDataReceived;
                _metaWearService.LightSensorDataReceived -= OnLightSensorDataReceived;
            }

            // Reset the flag
            _isNavigatingToGraphPage = false;
        }

        private void ShowDeviceInfo(Services.DeviceInfo info)
        {
            DeviceInfoFrame.IsVisible = true;
            DeviceControlsSection.IsVisible = true;
            SensorConfigSection.IsVisible = true;
            DeviceModelLabel.Text = info.Model ?? "-";
            DeviceSerialLabel.Text = info.SerialNumber ?? "-";
            DeviceHardwareLabel.Text = info.HardwareVersion ?? "-";
            DeviceBatteryLabel.Text = info.BatteryPercentage.HasValue ? $"{info.BatteryPercentage.Value}%" : "N/A";
            DeviceFirmwareLabel.Text = info.FirmwareVersion ?? "-";
            DeviceFirmwareStatusLabel.Text = "Up to date";
        }

        private void ClearDeviceInfo()
        {
            DeviceInfoFrame.IsVisible = false;
            DeviceControlsSection.IsVisible = false;
            SensorConfigSection.IsVisible = false;
            DeviceModelLabel.Text = "-";
            DeviceSerialLabel.Text = "-";
            DeviceHardwareLabel.Text = "-";
            DeviceBatteryLabel.Text = "-";
            DeviceFirmwareLabel.Text = "-";
            DeviceFirmwareStatusLabel.Text = "-";
        }

        /// <summary>
        /// Looks up the device by MAC. If new, prompts the user for a name and creates a default
        /// profile. Then loads all saved settings into SensorBufferManager and refreshes pickers.
        /// </summary>
        private async Task LoadOrCreateDeviceProfileAsync(string mac)
        {
            var device = await _deviceRepository.GetByMacAsync(mac);

            if (device == null)
            {
                // New device — ask the user for a friendly name
                string name = await DisplayPromptAsync(
                    "New SmartDot",
                    "Give this SmartDot a name:",
                    accept: "Save",
                    cancel: "Skip",
                    placeholder: "e.g. My MMS",
                    maxLength: 40);

                device = new SmartDotDevice
                {
                    MacAddress = mac,
                    DeviceName = string.IsNullOrWhiteSpace(name) ? mac : name.Trim(),
                    LastConnected = DateTime.Now
                    // All other properties stay at their model defaults
                };

                await _deviceRepository.AddAsync(device);
            }
            else
            {
                device.LastConnected = DateTime.Now;
                await _deviceRepository.UpdateAsync(device);
            }

            _currentDevice = device;

            // Push saved settings into SensorBufferManager so they take effect immediately
            SensorBufferManager.LightSensorHighThreshold      = device.LightSensorHighThreshold;
            SensorBufferManager.ContinuousSaveDurationSeconds = device.ContinuousSaveDurationSeconds;
            SensorBufferManager.LightSampleRate               = device.LightSampleRate;
            SensorBufferManager.LightGain                     = device.LightGain;
            SensorBufferManager.LightIntegrationTime          = device.LightIntegrationTime;
            SensorBufferManager.LightMeasurementRate          = device.LightMeasurementRate;
            SensorBufferManager.AccelSampleRate               = device.AccelSampleRate;
            SensorBufferManager.AccelRange                    = device.AccelRange;
            SensorBufferManager.GyroSampleRate                = device.GyroSampleRate;
            SensorBufferManager.GyroRange                     = device.GyroRange;
            SensorBufferManager.MagSampleRate                 = device.MagSampleRate;
            SensorBufferManager.BaroOversampling              = device.BaroOversampling;
            SensorBufferManager.BaroIirFilter                 = device.BaroIirFilter;
            SensorBufferManager.BaroStandbyTime               = device.BaroStandbyTime;

            // Refresh UI
            LightThresholdEntry.Text        = device.LightSensorHighThreshold.ToString("0");
            LightThresholdStatusLabel.Text  = $"Current threshold: {device.LightSensorHighThreshold:0}";
            RecordDurationEntry.Text        = device.ContinuousSaveDurationSeconds.ToString("0.##");
            RecordDurationStatusLabel.Text  = $"Current duration: {device.ContinuousSaveDurationSeconds:0.##}s";
            InitSensorConfigPickers();
        }

        /// <summary>
        /// Copies current SensorBufferManager/UI settings back into _currentDevice and persists.
        /// </summary>
        private async Task SaveCurrentDeviceSettingsAsync()
        {
            if (_currentDevice == null) return;

            _currentDevice.LightSensorHighThreshold      = SensorBufferManager.LightSensorHighThreshold;
            _currentDevice.ContinuousSaveDurationSeconds = SensorBufferManager.ContinuousSaveDurationSeconds;
            _currentDevice.LightSampleRate               = SensorBufferManager.LightSampleRate;
            _currentDevice.LightGain                     = SensorBufferManager.LightGain;
            _currentDevice.LightIntegrationTime          = SensorBufferManager.LightIntegrationTime;
            _currentDevice.LightMeasurementRate          = SensorBufferManager.LightMeasurementRate;
            _currentDevice.AccelSampleRate               = SensorBufferManager.AccelSampleRate;
            _currentDevice.AccelRange                    = SensorBufferManager.AccelRange;
            _currentDevice.GyroSampleRate                = SensorBufferManager.GyroSampleRate;
            _currentDevice.GyroRange                     = SensorBufferManager.GyroRange;
            _currentDevice.MagSampleRate                 = SensorBufferManager.MagSampleRate;
            _currentDevice.BaroOversampling              = SensorBufferManager.BaroOversampling;
            _currentDevice.BaroIirFilter                 = SensorBufferManager.BaroIirFilter;
            _currentDevice.BaroStandbyTime               = SensorBufferManager.BaroStandbyTime;

            await _deviceRepository.UpdateAsync(_currentDevice);
        }

        /// <summary>
        /// Saves the SmartDot MAC address to the current user's profile
        /// </summary>
        private async Task SaveSmartDotMacAsync(string macAddress)
        {
            try
            {
                int userId = Preferences.Get("UserId", -1);
                if (userId != -1)
                {
                    await _userRepository.UpdateSmartDotMacAsync(userId, macAddress);
                    System.Diagnostics.Debug.WriteLine($"Saved SmartDot MAC address: {macAddress} for user {userId}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving SmartDot MAC: {ex.Message}");
                // Don't show error to user - this is a background operation
            }
        }

        /// <summary>
        /// Updates the IsConnected status for the current user
        /// </summary>
        private async Task UpdateIsConnectedStatusAsync(bool isConnected)
        {
            try
            {
                int userId = Preferences.Get("UserId", -1);
                if (userId != -1)
                {
                    await _userRepository.UpdateIsConnectedAsync(userId, isConnected);
                    System.Diagnostics.Debug.WriteLine($"Updated IsConnected status: {isConnected} for user {userId}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating IsConnected status: {ex.Message}");
                // Don't show error to user - this is a background operation
            }
        }
    }

    public class BluetoothDevice : System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));

        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int Rssi { get; set; }
        public IDevice? Device { get; set; }

        private string? _savedName;
        public string? SavedName
        {
            get => _savedName;
            set { _savedName = value; OnPropertyChanged(nameof(SavedName)); OnPropertyChanged(nameof(DisplayName)); }
        }

        /// <summary>Returns the user's saved name for this device, falling back to the BLE name.</summary>
        public string DisplayName => !string.IsNullOrEmpty(_savedName) ? _savedName : Name;
    }

    public class SensorDataPoint
    {
        public DateTime Timestamp { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
    }
}
