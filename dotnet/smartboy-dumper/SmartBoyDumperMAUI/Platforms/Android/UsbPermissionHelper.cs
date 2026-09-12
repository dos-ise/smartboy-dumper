using System;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Hardware.Usb;
using Android.OS;
using Anotherlab.UsbSerialForAndroid.Driver;

namespace SmartBoyDumperMAUI.Platforms.Android
{
    public static class UsbPermissionHelper
    {
        private const string ActionUsbPermission = "com.dossoft.smartboydumpermaui.USB_PERMISSION";
        private const int VendorId = 0x16D0;
        private const int ProductId = 0x0557;

        private static ProbeTable BuildProbeTable()
        {
            var table = new ProbeTable();
            table.AddProduct(VendorId, ProductId, typeof(CdcAcmSerialDriver));
            return table;
        }

        /// <summary>
        /// Sucht unter allen aktuell angeschlossenen USB-Geräten genau das,
        /// das vom CdcAcmSerialDriver bedient werden kann (VID/PID-Match).
        /// </summary>
        public static UsbDevice? FindMatchingDevice(UsbManager manager)
        {
            var prober = new UsbSerialProber(BuildProbeTable());

            foreach (var device in manager.DeviceList.Values)
            {
                var driver = prober.ProbeDevice(device);
                if (driver != null)
                    return device; // exakt das Interface, das später auch geöffnet wird
            }

            return null;
        }

        /// <summary>
        /// Sucht das passende Gerät und fragt bei Bedarf die USB-Permission an.
        /// Wartet asynchron auf die Nutzer-Antwort im Permission-Dialog.
        /// </summary>
        public static async Task<UsbDevice?> FindAndRequestDeviceAsync(Context context)
        {
            var manager = (UsbManager)context.GetSystemService(Context.UsbService)!;
            var device = FindMatchingDevice(manager);
            if (device == null)
                return null;

            if (manager.HasPermission(device))
                return device;

            var tcs = new TaskCompletionSource<bool>();
            UsbPermissionReceiver? receiver = null;

            receiver = new UsbPermissionReceiver(grantedDevice =>
            {
                // Nur auf das Ergebnis für genau dieses Gerät reagieren
                bool granted = grantedDevice != null
                    && grantedDevice.DeviceName == device.DeviceName
                    && manager.HasPermission(device);

                tcs.TrySetResult(granted);
            });

            var filter = new IntentFilter(ActionUsbPermission);

            if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
                context.RegisterReceiver(receiver, filter, ReceiverFlags.NotExported);
            else
                context.RegisterReceiver(receiver, filter);

            try
            {
                var flags = Build.VERSION.SdkInt >= BuildVersionCodes.S
                    ? PendingIntentFlags.Mutable
                    : PendingIntentFlags.UpdateCurrent;

                // Explizit machen (Package setzen), sonst verbietet Android 14+
                // FLAG_MUTABLE bei impliziten Intents.
                var intent = new Intent(ActionUsbPermission).SetPackage(context.PackageName);

                var permissionIntent = PendingIntent.GetBroadcast(
                    context, 0, intent, flags);

                manager.RequestPermission(device, permissionIntent);

                bool granted = await tcs.Task;
                return granted ? device : null;
            }
            finally
            {
                context.UnregisterReceiver(receiver);
            }
        }

        private class UsbPermissionReceiver : BroadcastReceiver
        {
            private readonly Action<UsbDevice?> _onResult;

            public UsbPermissionReceiver(Action<UsbDevice?> onResult)
            {
                _onResult = onResult;
            }

            public override void OnReceive(Context? context, Intent? intent)
            {
                if (intent?.Action != ActionUsbPermission)
                    return;

                bool permissionGranted = intent.GetBooleanExtra(UsbManager.ExtraPermissionGranted, false);
                var device = (UsbDevice?)intent.GetParcelableExtra(UsbManager.ExtraDevice);

                _onResult(permissionGranted ? device : null);
            }
        }
    }
}