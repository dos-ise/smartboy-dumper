using Android.Hardware.Usb;
using Anotherlab.UsbSerialForAndroid.Driver;
using smartboy_dumper;

namespace SmartBoyDumperMAUI
{
    public class AndroidUsbSerialTransport : IByteTransport
    {
        private readonly UsbDeviceConnection _connection;
        private readonly UsbSerialPort _port;
        private readonly Queue<byte> _rx = new();
        private readonly byte[] _chunk = new byte[4096];

        public AndroidUsbSerialTransport(UsbManager manager, UsbDevice d)
        {
            UsbDevice? found = null;

            // Alle Geräte durchgehen
            foreach (var device in manager.DeviceList.Values)
            {
                // ProbeTable für CDC-ACM
                var table = new ProbeTable();
                table.AddProduct(0x16D0, 0x0557, typeof(CdcAcmSerialDriver));

                var prober = new UsbSerialProber(table);
                var driver = prober.ProbeDevice(device);

                if (driver != null)
                {
                    found = device;

                    _connection = manager.OpenDevice(driver.Device);

                    if (_connection == null)
                    {
                        throw new SmartboyException("Konnte USB-Verbindung nicht öffnen (Berechtigung fehlt?).");
                    }
                      
                    _port = driver.Ports[0];
                    _port.Open(_connection);
                    _port.SetParameters(115200, UsbSerialPort.DATABITS_8, StopBits.One, Parity.None);
                    _port.SetDTR(true);
                    _port.SetRTS(true);

                    return; // Erfolgreich -> Konstruktor verlassen
                }
            }

            throw new SmartboyException("Kein passender Seriell-Treiber für irgendein USB-Gerät gefunden.");
        }


        public byte ReadByte()
        {
            while (_rx.Count == 0)
            {
                int n = _port.Read(_chunk, 1000);
                for (int i = 0; i < n; i++)
                    _rx.Enqueue(_chunk[i]);
            }

            return _rx.Dequeue();
        }

        public void WriteBytes(byte[] data)
        {
            _port.Write(data, 1000);
        }

        public void Dispose()
        {
            try { _port?.Close(); } catch { /* ignorieren */ }
            try { _connection?.Close(); } catch { /* ignorieren */ }
        }
    }
}