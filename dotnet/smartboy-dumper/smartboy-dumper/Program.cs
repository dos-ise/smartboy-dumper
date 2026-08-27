using System;
using System.Linq;
using LibUsbDotNet.LibUsb;

namespace SmartboyDumperCs
{
    internal static class Program
    {
        private static int Main()
        {
            using var context = new UsbContext();
            var devices = context.List().ToList();

            if (devices.Count == 0)
            {
                Console.WriteLine("Keine USB-Geräte gefunden.");
                return 1;
            }

            Console.WriteLine("Gefundene USB-Geräte:");
            Console.WriteLine();

            for (int i = 0; i < devices.Count; i++)
            {
                var dev = devices[i];
                string name = "";

                try
                {
                    dev.Open();
                    string? mfg = dev.Info.Manufacturer;
                    string? prod = dev.Info.Product;
                    name = string.Join(" ", new[] { mfg, prod }.Where(s => !string.IsNullOrWhiteSpace(s)));
                }
                catch
                {
                    // manche Geräte lassen sich ohne Rechte/Treiber nicht öffnen - ignorieren
                }
                finally
                {
                    if (dev.IsOpen)
                        dev.Close();
                }

                Console.WriteLine($"  [{i}] {dev.VendorId:X4}:{dev.ProductId:X4}  {name}");
            }

            Console.WriteLine();
            Console.Write("Gerät per Nummer auswählen: ");
            string? input = Console.ReadLine();

            if (!int.TryParse(input, out int index) || index < 0 || index >= devices.Count)
            {
                Console.WriteLine("Ungültige Auswahl.");
                return 1;
            }

            var selected = devices[index];
            int vendorId = selected.VendorId;
            int productId = selected.ProductId;

            try
            {
                using var dumper = new SmartboyDumper(vendorId, productId) { Verbose = true };
                Console.WriteLine($"*** Gerät {vendorId:X4}:{productId:X4} geöffnet");
                dumper.Run();
            }
            catch (SmartboyException ex)
            {
                Console.WriteLine($"Fehler: {ex.Message}");
                return 1;
            }

            return 0;
        }
    }
}