using Android.Hardware.Usb;
using Anotherlab.UsbSerialForAndroid.Driver;
using smartboy_dumper;

public class AndroidUsbSerialTransport : IByteTransport
{
    private readonly UsbDeviceConnection _connection;
    private readonly UsbSerialPort _port;

    public AndroidUsbSerialTransport(UsbManager manager, UsbDevice device)
    {
        var table = new ProbeTable();
        table.AddProduct(0x16D0, 0x0557, typeof(CdcAcmSerialDriver));

        var prober = new UsbSerialProber(table);
        var driver = prober.ProbeDevice(device)
                     ?? throw new SmartboyException("Kein passender Seriell-Treiber gefunden.");

        _connection = manager.OpenDevice(driver.Device)
                      ?? throw new SmartboyException("Konnte USB-Verbindung nicht öffnen.");

        _port = driver.Ports[0];
        _port.Open(_connection);
        _port.SetParameters(115200, UsbSerialPort.DATABITS_8, StopBits.One, Parity.None);
        _port.SetDTR(true);
        _port.SetRTS(true);
    }

    public byte ReadByte()
    {
        var buf = new byte[1];

        while (true)
        {
            int n = _port.Read(buf, 1000);
            if (n == 1)
                return buf[0];
        }
    }

    public void WriteBytes(byte[] data)
    {
        _port.Write(data, 1000);
    }

    public void Dispose()
    {
        try { _port?.Close(); } catch { }
        try { _connection?.Close(); } catch { }
    }
}