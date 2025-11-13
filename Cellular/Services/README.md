# MetaWear Service Implementation

## Overview
This directory contains the cross-platform MetaWear service implementation for MAUI .NET. The service provides access to MetaWear MMS devices across Android, iOS, Windows, and macOS.

## Architecture

### Platform Abstraction
- **IMetaWearService**: Platform-agnostic interface for MetaWear operations
- **MetaWearBleService**: BLE-based implementation using Plugin.BLE

### Current Implementation
The current implementation (`MetaWearBleService`) uses **Plugin.BLE** to communicate directly with MetaWear devices via BLE GATT. This approach:
- Works across all MAUI platforms (Android, iOS, Windows, macOS)
- Doesn't require native bindings
- Implements the MetaWear protocol directly

### Limitations
The current BLE-based implementation is a **simplified version** that:
- Uses basic MetaWear GATT services and characteristics
- Implements core accelerometer and gyroscope functionality
- May need adjustments based on your specific MetaWear firmware version

### Future Enhancements

#### Option 1: Full Native Bindings (Recommended for Production)
For production use, consider implementing platform-specific native bindings:
- **Android**: Use Java Interop to bind to the [Java SDK](https://github.com/mbientlab/MetaWear-SDK-Android)
- **iOS**: Use Objective-C/Swift bindings for the [iOS SDK](https://github.com/mbientlab/MetaWear-SDK-iOS-macOS-tvOS)
- **Windows**: Use C++ API via P/Invoke or create a C++/CLI wrapper for the [C++ SDK](https://github.com/mbientlab/MetaWear-SDK-Cpp)

#### Option 2: Protocol Implementation
Implement the full MetaWear protocol by:
- Referencing the [C++ API documentation](https://mbientlab.com/cppdocs/latest/)
- Implementing all MetaWear commands and data parsing
- Adding support for all sensors and features

## Usage

### Dependency Injection
The service is registered in `MauiProgram.cs`:
```csharp
builder.Services.AddSingleton<IMetaWearService, MetaWearBleService>();
```

### Basic Usage
```csharp
// Get service from DI
var metaWearService = Handler.MauiContext.Services.GetService<IMetaWearService>();

// Connect to device
await metaWearService.ConnectAsync(deviceId, deviceName);

// Start accelerometer
metaWearService.AccelerometerDataReceived += OnAccelerometerData;
await metaWearService.StartAccelerometerAsync(50f, 16f);

// Stop accelerometer
await metaWearService.StopAccelerometerAsync();

// Disconnect
await metaWearService.DisconnectAsync();
```

## MetaWear GATT Services

### Service UUIDs
- **MetaWear Service**: `326A9000-85CB-9195-D9DD-464CFBBAE75A`
- **Command Characteristic**: `326A9001-85CB-9195-D9DD-464CFBBAE75A`
- **Notification Characteristic**: `326A9002-85CB-9195-D9DD-464CFBBAE75A`

### Device Information Service
- **Service UUID**: `0000180a-0000-1000-8000-00805f9b34fb`
- Standard BLE Device Information Service characteristics

## Notes

1. **Device Discovery**: MetaWear devices typically advertise with "MetaWear" or "MMS" in the device name
2. **Connection**: Devices must be scanned first before connection
3. **Sensor Data**: Accelerometer and gyroscope data parsing may need adjustment based on firmware version
4. **Commands**: MetaWear commands follow a specific protocol - refer to C++ API docs for full command structure

## Resources

- [MetaWear API Documentation](https://mbientlab.com/tutorials/MetaWearAPI.html)
- [C++ API Documentation](https://mbientlab.com/cppdocs/latest/)
- [Java SDK (Android)](https://github.com/mbientlab/MetaWear-SDK-Android)
- [Swift SDK (iOS)](https://github.com/mbientlab/MetaWear-SDK-iOS-macOS-tvOS)
- [C++ SDK](https://github.com/mbientlab/MetaWear-SDK-Cpp)

