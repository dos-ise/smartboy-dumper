using System;

namespace SmartboyDumperCs
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            int vendorId = 0x0000;   // TODO: per lsusb -v ermitteln
            int productId = 0x0000;  // TODO: per lsusb -v ermitteln
            bool verbose = false;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "-v":
                    case "--verbose":
                        verbose = true;
                        break;
                    case "--vid":
                        vendorId = Convert.ToInt32(args[++i], 16);
                        break;
                    case "--pid":
                        productId = Convert.ToInt32(args[++i], 16);
                        break;
                    default:
                        Console.WriteLine($"Unbekannte Option: {args[i]}");
                        return 1;
                }
            }

            if (vendorId == 0 || productId == 0)
            {
                Console.WriteLine("Bitte --vid und --pid angeben, z.B.:");
                Console.WriteLine("  SmartboyDumperCs --vid 0483 --pid 5740 -v");
                return 1;
            }

            try
            {
                using var dumper = new SmartboyDumper(vendorId, productId) { Verbose = verbose };
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
