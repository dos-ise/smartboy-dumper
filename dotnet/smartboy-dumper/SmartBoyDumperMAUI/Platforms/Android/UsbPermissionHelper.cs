using System;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Hardware.Usb;
using AndroidX.Core.Content;

namespace SmartBoyDumperMAUI.Platforms.Android
{
    public static class UsbPermissionHelper
    {
        private const string ActionUsbPermission = "com.deinefirma.smartboydumper.USB_PERMISSION";

        public static Task<UsbDevice?> FindAndRequestDeviceAsync(Context context)
        {
            var manager = (UsbManager)context.GetSystemService(Context.UsbService)!;
            var tcs = new TaskCompletionSource<UsbDevice?>();

            UsbDevice? target = null;
            foreach (var device in manager.DeviceList!.Values)
            {
                // Der Smartboy meldet sich als CDC-ACM-Interface - hier grob nach
                // Vendor-Klasse filtern, notfalls Liste aller Geräte anzeigen lassen.
                target = device;
                break;
            }

            if (target == null)
            {
                tcs.SetResult(null);
                return tcs.Task;
            }

            if (manager.HasPermission(target))
            {
                tcs.SetResult(target);
                return tcs.Task;
            }

            var receiver = new UsbPermissionReceiver(granted =>
            {
                tcs.TrySetResult(granted ? target : null);
            });

            context.RegisterReceiver(receiver, new IntentFilter(ActionUsbPermission),
                (ReceiverFlags)ContextCompat.ReceiverNotExported);

            // Expliziter Intent für Android 14+
            var intent = new Intent(context, typeof(UsbPermissionReceiver));
            intent.SetAction(ActionUsbPermission);

            // Immutable statt Mutable
            var permissionIntent = PendingIntent.GetBroadcast(
                context,
                0,
                intent,
                PendingIntentFlags.Immutable
            );

            manager.RequestPermission(target, permissionIntent);


            return tcs.Task;
        }

        private class UsbPermissionReceiver : BroadcastReceiver
        {
            private readonly Action<bool> _callback;

            public UsbPermissionReceiver(Action<bool> callback) => _callback = callback;

            public override void OnReceive(Context? context, Intent? intent)
            {
                if (intent?.Action != ActionUsbPermission) return;

                bool granted = intent.GetBooleanExtra(UsbManager.ExtraPermissionGranted, false);
                _callback(granted);
                context?.UnregisterReceiver(this);
            }
        }
    }
}