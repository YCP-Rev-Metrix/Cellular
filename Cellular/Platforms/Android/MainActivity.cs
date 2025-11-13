using Android.App;
using Android.Content.PM;
using Android.OS;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using AndroidX.Core.App;

namespace Cellular
{
    [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {
        // Permission request code for Bluetooth
        public const int BluetoothPermissionRequestCode = 1001;
        
        // Dictionary to store permission request completion sources
        private static Dictionary<int, TaskCompletionSource<Permission[]>> _permissionCompletions = new();

        public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
        {
            base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
            
            // Notify waiting tasks about the permission result
            if (_permissionCompletions.TryGetValue(requestCode, out var completionSource))
            {
                _permissionCompletions.Remove(requestCode);
                completionSource.TrySetResult(grantResults);
            }
        }

        public static Task<Permission[]> RequestPermissionsAsync(string[] permissions, int requestCode)
        {
            var completionSource = new TaskCompletionSource<Permission[]>();
            _permissionCompletions[requestCode] = completionSource;
            
            var activity = Platform.CurrentActivity as MainActivity;
            if (activity != null)
            {
                AndroidX.Core.App.ActivityCompat.RequestPermissions(activity, permissions, requestCode);
            }
            else
            {
                completionSource.TrySetException(new InvalidOperationException("Activity not available"));
            }
            
            return completionSource.Task;
        }
    }
}
