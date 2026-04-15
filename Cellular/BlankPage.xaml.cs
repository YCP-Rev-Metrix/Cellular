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
        private readonly UserRepository _userRepository;
        private readonly SessionRepository _sessionRepository;
        private readonly BallRepository _ballRepository;
        private readonly EventRepository _eventRepository;
        private readonly GameRepository _gameRepository;
        private readonly FrameRepository _frameRepository;
        private readonly ShotRepository _shotRepository;
        private readonly EstablishmentRepository _establishmentRepository;

        private ObservableCollection<BluetoothDeviceWatch> _devices;
        private BluetoothDeviceWatch? _selectedDevice;
        private bool _isScanning;
        private bool _isConnected;
        private string _watchJson = "No data received yet.";
        private string? _defaultWatchName;
        private string? _defaultWatchMac;

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

            var dbConnection = new CellularDatabase().GetConnection();
            _userRepository = new UserRepository(dbConnection);
            _sessionRepository = new SessionRepository(dbConnection);
            _ballRepository = new BallRepository(dbConnection);
            _eventRepository = new EventRepository(dbConnection);
            _gameRepository = new GameRepository(dbConnection);
            _frameRepository = new FrameRepository(dbConnection);
            _shotRepository = new ShotRepository(dbConnection);
            _establishmentRepository = new EstablishmentRepository(dbConnection);

            // Get userId for sync context initialization
            int userId = Preferences.Get("UserId", -1);

            // Initialize the watch BLE service with repositories for shot packet handling and sync context
            // User will be fetched on-demand when needed for sending data to watch
            _watchBleService.SetRepositories(_gameRepository, _frameRepository, _shotRepository,
                _sessionRepository, _ballRepository, _eventRepository, null, userId, _establishmentRepository);

            Devices = BlankPageStore.SavedDevices ?? new ObservableCollection<BluetoothDeviceWatch>();
            _selectedDevice = BlankPageStore.SavedSelected;
            _isConnected = BlankPageStore.SavedIsConnected;
            _watchBleService.WatchJsonReceived += OnWatchJsonReceived;
            _watchBleService.WatchDisconnected += OnDeviceDisconnected;
            BindingContext = this;

            DeviceListView.SelectionChanged += OnDeviceSelected;

            if (_isConnected && _selectedDevice != null)
            {
                StatusLabel.Text = $"Connected to {_selectedDevice.Name}";
                DeviceInfoLabel.Text = BlankPageStore.SavedDeviceInfo;
            }

            // Load default watch
            LoadDefaultWatch();
        }

        private async void LoadDefaultWatch()
        {
            try
            {
                int userId = Preferences.Get("UserId", -1);
                if (userId != -1)
                {
                    var (name, mac) = await _userRepository.GetDefaultWatchAsync(userId);
                    if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(mac))
                    {
                        _defaultWatchName = name;
                        _defaultWatchMac = mac;

                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            DefaultWatchStack.IsVisible = true;
                            DefaultWatchButton.Text = $"Connect to {name}";
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading default watch: {ex.Message}");
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
                await DisplayAlertAsync("Bluetooth", "Please enable Bluetooth", "OK");
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
                            await DisplayAlertAsync("Permission Required",
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
                        await DisplayAlertAsync("Permission Required",
                            "Location permission is required.",
                            "OK");
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                await DisplayAlertAsync("Permission Error", ex.Message, "OK");
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
                await DisplayAlertAsync("Connect", "Please select a device first", "OK");
                return;
            }

            try
            {
                if (SelectedDevice.Device == null)
                {
                    await DisplayAlertAsync("Connect", "Device object missing", "OK");
                    return;
                }

                StatusLabel.Text = "Connecting...";

                bool connected = await _watchBleService.ConnectAsync(SelectedDevice.Device);

                if (connected)
                {
                    IsConnected = true;
                    StatusLabel.Text = $"Connected to {SelectedDevice.Name}";

                    // Ask if user wants this as default watch
                    bool makeDefault = await DisplayAlert("Default Watch", 
                        $"Make {SelectedDevice.Name} your default watch?", 
                        "Yes", "No");

                    if (makeDefault)
                    {
                        await SetAsDefaultWatch(SelectedDevice.Name, SelectedDevice.Id);
                    }

                    await SendUserDataToWatch();
                }
                else
                {
                    StatusLabel.Text = "Connection failed";
                }
            }
            catch (Exception ex)
            {
                await DisplayAlertAsync("Connect Error", ex.Message, "OK");
            }
        }

        private async Task SendUserDataToWatch()
        {
            try
            {
                int userId = Preferences.Get("UserId", -1);
                if (userId == -1)
                {
                    System.Diagnostics.Debug.WriteLine("PHONE BLE SEND → No user logged in");
                    return;
                }

                var user = await _userRepository.GetUserByIdAsync(userId);
                if (user == null)
                {
                    System.Diagnostics.Debug.WriteLine("PHONE BLE SEND → User not found");
                    return;
                }

                System.Diagnostics.Debug.WriteLine($"PHONE BLE SEND → Sending binary packet for user {user.UserName}");

                bool success = await _watchBleService.SendJsonToWatch(userId, _sessionRepository, _ballRepository, _eventRepository, _gameRepository, user, _frameRepository, _shotRepository, _establishmentRepository);

                if (success)
                {
                    System.Diagnostics.Debug.WriteLine("PHONE BLE SEND → Success");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("PHONE BLE SEND → Failed");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"PHONE BLE SEND → Error: {ex.Message}");
                await DisplayAlert("Data Send Error", ex.Message, "OK");
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
                await DisplayAlertAsync("Disconnect Error", ex.Message, "OK");
            }
        }

        private async void OnSendToWatchClicked(object sender, EventArgs e)
        {
            if (!_watchBleService.IsConnected)
            {
                await DisplayAlertAsync("BLE", "Not connected", "OK");
                return;
            }

            try
            {
                int userId = Preferences.Get("UserId", -1);
                if (userId == -1)
                {
                    await DisplayAlertAsync("BLE", "No user logged in", "OK");
                    return;
                }

                var user = await _userRepository.GetUserByIdAsync(userId);
                bool success = await _watchBleService.SendJsonToWatch(userId, _sessionRepository, _ballRepository, _eventRepository, _gameRepository, user, _frameRepository, _shotRepository, _establishmentRepository);

                if (!success)
                    await DisplayAlertAsync("BLE", "Failed to send data", "OK");
            }
            catch (Exception ex)
            {
                await DisplayAlertAsync("BLE", $"Error: {ex.Message}", "OK");
            }
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

        private async Task SetAsDefaultWatch(string watchName, string watchMac)
        {
            try
            {
                int userId = Preferences.Get("UserId", -1);
                if (userId != -1)
                {
                    await _userRepository.SetDefaultWatchAsync(userId, watchName, watchMac);

                    _defaultWatchName = watchName;
                    _defaultWatchMac = watchMac;

                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        DefaultWatchStack.IsVisible = true;
                        DefaultWatchButton.Text = $"Connect to {watchName}";
                    });

                    await DisplayAlert("Default Watch", $"{watchName} is now your default watch!", "OK");
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Failed to set default watch: {ex.Message}", "OK");
            }
        }

        private async void OnDefaultWatchConnectClicked(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_defaultWatchMac))
            {
                await DisplayAlert("Error", "No default watch set", "OK");
                return;
            }

            try
            {
                StatusLabel.Text = "Scanning for default watch...";

                // Show connecting state
                DefaultWatchButton.IsEnabled = false;

                // Scan for the default watch device
                bool deviceFound = false;
                IDevice? targetDevice = null;

                EventHandler<DeviceEventArgs> handler = null;
                handler = (s, e) =>
                {
                    if (e.Device != null && e.Device.Id.ToString().Equals(_defaultWatchMac, StringComparison.OrdinalIgnoreCase))
                    {
                        deviceFound = true;
                        targetDevice = e.Device;
                        _adapter.DeviceDiscovered -= handler;
                    }
                };

                _adapter.DeviceDiscovered += handler;

                await _adapter.StartScanningForDevicesAsync();

                // Wait up to 5 seconds for device discovery
                for (int i = 0; i < 50 && !deviceFound; i++)
                {
                    await Task.Delay(100);
                }

                await _adapter.StopScanningForDevicesAsync();

                if (targetDevice != null)
                {
                    // Connect to the device
                    bool connected = await _watchBleService.ConnectAsync(targetDevice);

                    if (connected)
                    {
                        IsConnected = true;
                        StatusLabel.Text = $"Connected to {_defaultWatchName}";
                        SelectedDevice = new BluetoothDeviceWatch
                        {
                            Id = targetDevice.Id.ToString(),
                            Name = targetDevice.Name ?? _defaultWatchName ?? "Unknown",
                            Device = targetDevice
                        };

                        await SendUserDataToWatch();
                    }
                    else
                    {
                        StatusLabel.Text = "Connection failed";
                        await DisplayAlert("Error", "Failed to connect to default watch", "OK");
                    }
                }
                else
                {
                    StatusLabel.Text = "Default watch not found";
                    await DisplayAlert("Error", $"Could not find {_defaultWatchName}. Make sure it's powered on and nearby.", "OK");
                }

                DefaultWatchButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                StatusLabel.Text = "Connection error";
                await DisplayAlert("Error", ex.Message, "OK");
                DefaultWatchButton.IsEnabled = true;
            }
        }

        private async void OnChangeDefaultWatchClicked(object sender, EventArgs e)
        {
            bool confirm = await DisplayAlert("Remove Default Watch", 
                "Remove as default watch?", 
                "Yes", "No");

            if (confirm)
            {
                // Reset default watch
                int userId = Preferences.Get("UserId", -1);
                if (userId != -1)
                {
                    await _userRepository.SetDefaultWatchAsync(userId, "", "");
                }

                _defaultWatchName = null;
                _defaultWatchMac = null;

                DefaultWatchStack.IsVisible = false;

                await DisplayAlert("Success", "Default watch removed", "OK");
            }
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
