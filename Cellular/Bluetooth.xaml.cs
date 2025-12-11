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
#if ANDROID
using Android;
using Android.Content.PM;
using AndroidX.Core.Content;
using AndroidX.Core.App;
#endif

namespace Cellular
{
    public partial class Bluetooth : ContentPage
    {
        private readonly IBluetoothLE _ble = CrossBluetoothLE.Current;
        private readonly IAdapter _adapter = CrossBluetoothLE.Current.Adapter;
        private readonly IMetaWearService _metaWearService;
        
        private ObservableCollection<BluetoothDevice> _devices;
        private BluetoothDevice? _selectedDevice;
        private bool _isScanning;
        private bool _isConnected;
        private bool _isNavigatingToGraphPage = false;
        
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

        public Bluetooth()
        {
            InitializeComponent();
            
            // Get MetaWear service from dependency injection
            _metaWearService = Handler?.MauiContext?.Services.GetService<IMetaWearService>() 
                ?? new MetaWearBleService();
            
            Devices = new ObservableCollection<BluetoothDevice>();
            
            // Subscribe to MetaWear service events
            _metaWearService.DeviceDisconnected += OnDeviceDisconnected;
            _metaWearService.AccelerometerDataReceived += OnAccelerometerDataReceived;
            _metaWearService.GyroscopeDataReceived += OnGyroscopeDataReceived;
            _metaWearService.MagnetometerDataReceived += OnMagnetometerDataReceived;
            _metaWearService.LightSensorDataReceived += OnLightSensorDataReceived;
            
            // Handle device selection
            DeviceListView.SelectionChanged += OnDeviceSelected;
            
            // Handle navigation events to detect when returning from graph page
            this.Appearing += OnPageAppearing;
            
            BindingContext = this;
        }

        private void OnPageAppearing(object? sender, EventArgs e)
        {
            // Reset the flag when page appears (user might have navigated back)
            _isNavigatingToGraphPage = false;
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
            MainThread.BeginInvokeOnMainThread(() =>
            {
                IsConnected = false;
                StatusLabel.Text = "Disconnected";
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
                await DisplayAlert("Bluetooth", "Please enable Bluetooth", "OK");
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
                                    await DisplayAlert("Permission Required", 
                                        "Bluetooth permission is required to scan for Bluetooth devices. Please grant BLUETOOTH_SCAN and BLUETOOTH_CONNECT permissions in your device settings.\n\nYou can do this by going to:\nSettings > Apps > Cellular > Permissions", 
                                        "OK");
                                    return;
                                }
                            }
                        }
                        catch (Exception permEx)
                        {
                            System.Diagnostics.Debug.WriteLine($"Permission request exception: {permEx.Message}");
                            await DisplayAlert("Permission Error", 
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
                await DisplayAlert("Scan Error", ex.Message, "OK");
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

            // Filter for MetaWear devices (MMS typically has "MetaWear" in the name)
            if (string.IsNullOrEmpty(e.Device.Name) || 
                (!e.Device.Name.Contains("MetaWear", StringComparison.OrdinalIgnoreCase) && 
                 !e.Device.Name.Contains("MMS", StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            MainThread.BeginInvokeOnMainThread(() =>
            {
                var existingDevice = Devices.FirstOrDefault(d => d.Id == e.Device.Id.ToString());
                if (existingDevice == null)
                {
                    Devices.Add(new BluetoothDevice
                    {
                        Id = e.Device.Id.ToString(),
                        Name = e.Device.Name ?? "Unknown",
                        Rssi = e.Device.Rssi,
                        Device = e.Device
                    });
                }
                else
                {
                    existingDevice.Rssi = e.Device.Rssi;
                    existingDevice.Device = e.Device; // Update device object
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
                await DisplayAlert("Connect", "Please select a device first", "OK");
                return;
            }

            try
            {
                if (SelectedDevice.Device == null)
                {
                    await DisplayAlert("Connect", "Device object is not available. Please scan for devices again.", "OK");
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
                    
                    // Get device info
                    try
                    {
                        var deviceInfo = await _metaWearService.GetDeviceInfoAsync();
                        DeviceInfoLabel.Text = $"Model: {deviceInfo.Model}, Firmware: {deviceInfo.FirmwareVersion}";
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error getting device info: {ex.Message}");
                        DeviceInfoLabel.Text = "Connected (device info unavailable)";
                    }
                }
                else
                {
                    var errorMessage = $"Failed to connect to {SelectedDevice.Name}.\n\n" +
                        "Possible issues:\n" +
                        "• MetaWear service or characteristics not found\n" +
                        "• Device may be in use by another app\n" +
                        "• Check Debug output for detailed error messages";
                    await DisplayAlert("Connection Failed", errorMessage, "OK");
                    StatusLabel.Text = "Connection failed - check Debug logs";
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Connect Error", ex.Message, "OK");
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
                DeviceInfoLabel.Text = "";
                AccelerometerLabel.Text = "";
                GyroscopeLabel.Text = "";
                MagnetometerLabel.Text = "";
                LightSensorLabel.Text = "";
            }
            catch (Exception ex)
            {
                await DisplayAlert("Disconnect Error", ex.Message, "OK");
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
                await DisplayAlert("Error", ex.Message, "OK");
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
                await DisplayAlert("Error", ex.Message, "OK");
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
                await DisplayAlert("Error", ex.Message, "OK");
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
                await DisplayAlert("Error", ex.Message, "OK");
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
                await DisplayAlert("Error", ex.Message, "OK");
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
                await DisplayAlert("Error", ex.Message, "OK");
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
                await DisplayAlert("Error", ex.Message, "OK");
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
                await DisplayAlert("Error", ex.Message, "OK");
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
                await DisplayAlert("Probe Error", ex.Message, "OK");
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

        protected override async void OnDisappearing()
        {
            base.OnDisappearing();
            
            // Check if we're navigating to SensorGraphPage - if so, don't disconnect
            var navigationStack = Navigation.NavigationStack;
            bool isNavigatingToGraph = navigationStack.Any(p => p is SensorGraphPage);
            
            // Also check if SensorGraphPage is being pushed (it might not be in stack yet)
            // We'll use a flag to track this
            if (!isNavigatingToGraph && !_isNavigatingToGraphPage)
            {
                // Disconnect from device when leaving the page (but not when going to graph page)
                if (_metaWearService.IsConnected)
                {
                    try
                    {
                        // Stop all sensors first
                        await _metaWearService.StopAccelerometerAsync();
                        await _metaWearService.StopGyroscopeAsync();
                        await _metaWearService.StopMagnetometerAsync();
                        await _metaWearService.StopLightSensorAsync();
                        
                        // Then disconnect
                        await _metaWearService.DisconnectAsync();
                    }
                    catch (Exception ex)
                    {
                        // Log error but don't block navigation
                        System.Diagnostics.Debug.WriteLine($"Error disconnecting on page disappear: {ex.Message}");
                    }
                }
                
                // Unsubscribe from events only if we're actually leaving (not going to graph)
                _metaWearService.DeviceDisconnected -= OnDeviceDisconnected;
                _metaWearService.AccelerometerDataReceived -= OnAccelerometerDataReceived;
                _metaWearService.GyroscopeDataReceived -= OnGyroscopeDataReceived;
                _metaWearService.MagnetometerDataReceived -= OnMagnetometerDataReceived;
                _metaWearService.LightSensorDataReceived -= OnLightSensorDataReceived;
            }
            
            // Reset the flag
            _isNavigatingToGraphPage = false;
        }
    }

    public class BluetoothDevice
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int Rssi { get; set; }
        public IDevice? Device { get; set; }
    }

    public class SensorDataPoint
    {
        public DateTime Timestamp { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
    }
}
