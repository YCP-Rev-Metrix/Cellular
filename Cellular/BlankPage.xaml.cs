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
    public static class BlankPageStore
        {
            public static ObservableCollection<BluetoothDeviceWatch>? SavedDevices;
            public static BluetoothDeviceWatch? SavedSelected;
            public static bool SavedIsConnected;
            public static string SavedDeviceInfo = "";
        }

    public partial class BlankPage : ContentPage
    {
        private readonly IBluetoothLE _ble = CrossBluetoothLE.Current;
        private readonly IAdapter _adapter = CrossBluetoothLE.Current.Adapter;
        private readonly IWatchBleService _watchBleService;

        private ObservableCollection<BluetoothDeviceWatch> _devices;
        private BluetoothDeviceWatch? _selectedDevice;
        private bool _isScanning;
        private bool _isConnected;
        private string _watchJson = "No data received yet.";

        public ObservableCollection<BluetoothDeviceWatch> Devices
        {
            get => _devices;
            set
            {
                _devices = value;
                OnPropertyChanged();
            }
        }

        public BluetoothDeviceWatch? SelectedDevice
        {
            get => _selectedDevice;
            set
            {
                _selectedDevice = value;
                BlankPageStore.SavedSelected = value; // PERSIST
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanConnect));
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
                BlankPageStore.SavedIsConnected = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanDisconnect));
            }
        }

        public bool CanConnect => SelectedDevice != null && !IsConnected && !IsScanning;
        public bool CanDisconnect => IsConnected;

        public BlankPage(IWatchBleService watchBleService)
        {
            InitializeComponent();
            _watchBleService = watchBleService;
            Devices = BlankPageStore.SavedDevices ?? new ObservableCollection<BluetoothDeviceWatch>();
            _selectedDevice = BlankPageStore.SavedSelected;
            _isConnected = BlankPageStore.SavedIsConnected;
            _watchBleService.WatchJsonReceived += OnWatchJsonReceived;
            BindingContext = this;

            DeviceListView.SelectionChanged += OnDeviceSelected;

            if (_isConnected && _selectedDevice != null)
            {
                StatusLabel.Text = $"Connected to {_selectedDevice.Name}";
                DeviceInfoLabel.Text = BlankPageStore.SavedDeviceInfo;
            }
        }
        private void OnWatchJsonReceived(object? sender, string json)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                _watchJson = json;
                WatchJsonLabel.Text = json; //for displaying json on the page ong
            });
        }

        private void OnDeviceSelected(object? sender, SelectionChangedEventArgs e)
        {
            if (DeviceListView.SelectedItem is BluetoothDeviceWatch device)
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
                DeviceInfoLabel.Text = "";
                BlankPageStore.SavedDeviceInfo = "";
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
            if (!_ble.IsOn)
            {
                await DisplayAlert("Bluetooth", "Please enable Bluetooth", "OK");
                return;
            }

#if ANDROID
            try
            {
                var androidVersion = (int)Android.OS.Build.VERSION.SdkInt;

                if (androidVersion >= 31)
                {
                    var activity = Platform.CurrentActivity ??
                                   throw new InvalidOperationException("Android activity not available");

                    var bluetoothScanPermission = Manifest.Permission.BluetoothScan;
                    var bluetoothConnectPermission = Manifest.Permission.BluetoothConnect;

                    var scanGranted = ContextCompat.CheckSelfPermission(activity, bluetoothScanPermission) ==
                                      Permission.Granted;
                    var connectGranted = ContextCompat.CheckSelfPermission(activity, bluetoothConnectPermission) ==
                                         Permission.Granted;

                    if (!scanGranted || !connectGranted)
                    {
                        var permissions = new List<string>();
                        if (!scanGranted) permissions.Add(bluetoothScanPermission);
                        if (!connectGranted) permissions.Add(bluetoothConnectPermission);

                        var grantResults = await MainActivity.RequestPermissionsAsync(
                            permissions.ToArray(),
                            MainActivity.BluetoothPermissionRequestCode);

                        var allGranted = grantResults.Length > 0 &&
                                         grantResults.All(r => r == Permission.Granted);

                        if (!allGranted)
                        {
                            await DisplayAlert("Permission Required",
                                "Bluetooth permission is required.",
                                "OK");
                            return;
                        }
                    }
                }
                else
                {
                    var locationStatus = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
                    if (locationStatus != PermissionStatus.Granted)
                    {
                        await DisplayAlert("Permission Required",
                            "Location permission is required.",
                            "OK");
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Permission Error", ex.Message, "OK");
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
                await _adapter.StartScanningForDevicesAsync();

                _ = Task.Delay(TimeSpan.FromSeconds(10)).ContinueWith(_ =>
                {
                    if (IsScanning)
                    {
                        MainThread.BeginInvokeOnMainThread(async () => { await StopScanningAsync(); });
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
                _adapter.DeviceDiscovered -= OnDeviceDiscovered;
                _adapter.ScanTimeoutElapsed -= OnScanTimeout;

                await _adapter.StopScanningForDevicesAsync();

                IsScanning = false;
                ScanButton.Text = "Scan";
                StatusLabel.Text = $"Found {Devices.Count} device(s)";
                BlankPageStore.SavedDevices = Devices;
            }
            catch
            {
                IsScanning = false;
                ScanButton.Text = "Scan";
                StatusLabel.Text = "Scan stopped";
            }
        }

        private void OnDeviceDiscovered(object? sender, DeviceEventArgs e)
        {
            if (e.Device == null) return;

            if (string.IsNullOrEmpty(e.Device.Name) ||
                (!e.Device.Name.Contains("Galaxy", StringComparison.OrdinalIgnoreCase) &&
                 !e.Device.Name.Contains("Watch", StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            MainThread.BeginInvokeOnMainThread(() =>
            {
                var existing = Devices.FirstOrDefault(d => d.Id == e.Device.Id.ToString());
                if (existing == null)
                {
                    Devices.Add(new BluetoothDeviceWatch
                    {
                        Id = e.Device.Id.ToString(),
                        Name = e.Device.Name ?? "Unknown",
                        Rssi = e.Device.Rssi,
                        Device = e.Device
                    });
                }
                else
                {
                    existing.Rssi = e.Device.Rssi;
                    existing.Device = e.Device;
                }

                BlankPageStore.SavedDevices = Devices;
            });
        }

        private void OnScanTimeout(object? sender, EventArgs e)
        {
            MainThread.BeginInvokeOnMainThread(async () => await StopScanningAsync());
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
                    await DisplayAlert("Connect", "Device object missing", "OK");
                    return;
                }

                StatusLabel.Text = "Connecting...";

                bool connected = await _watchBleService.ConnectAsync(SelectedDevice.Device);

                if (connected)
                {
                    IsConnected = true;
                    StatusLabel.Text = $"Connected to {SelectedDevice.Name}";
                }
                else
                {
                    StatusLabel.Text = "Connection failed";
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Connect Error", ex.Message, "OK");
            }
        }

        private async void OnDisconnectClicked(object sender, EventArgs e)
        {
            try
            {
                await _watchBleService.DisconnectAsync();

                IsConnected = false;
                StatusLabel.Text = "Disconnected";
                DeviceInfoLabel.Text = "";
                BlankPageStore.SavedDeviceInfo = "";
            }
            catch (Exception ex)
            {
                await DisplayAlert("Disconnect Error", ex.Message, "OK");
            }
        }

        private async void OnSendToWatchClicked(object sender, EventArgs e)
        {
            if (!_watchBleService.IsConnected)
            {
                await DisplayAlert("BLE", "Not connected", "OK");
                return;
            }

            var json = new { message = "6" };

            bool success = await _watchBleService.SendJsonToWatch(json);

            if (!success)
                await DisplayAlert("BLE", "Failed to send JSON", "OK");
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();

            // save latest state so it doesnt go away when i leave
            BlankPageStore.SavedDevices = Devices;
            BlankPageStore.SavedSelected = SelectedDevice;
            BlankPageStore.SavedIsConnected = IsConnected;
            BlankPageStore.SavedDeviceInfo = DeviceInfoLabel.Text;
        }
    }

    public class BluetoothDeviceWatch
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int Rssi { get; set; }
        public IDevice? Device { get; set; }
    }
}
