using System;
using System.IO.Ports;
using System.Linq;

namespace SmartboyDumperCs
{
    internal static class Program
    {
        private static int Main()
        {
            var ports = SerialPort.GetPortNames()
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (ports.Count == 0)
            {
                Console.WriteLine("Keine COM-Ports gefunden.");
                return 1;
            }

            Console.WriteLine("Gefundene COM-Ports:");
            Console.WriteLine();

            for (int i = 0; i < ports.Count; i++)
                Console.WriteLine($"  [{i}] {ports[i]}");

            Console.WriteLine();
            Console.Write("Port per Nummer auswählen: ");
            string? input = Console.ReadLine();

            if (!int.TryParse(input, out int index) || index < 0 || index >= ports.Count)
            {
                Console.WriteLine("Ungültige Auswahl.");
                return 1;
            }

            string portName = ports[index];

            try
            {
                using var dumper = new SmartboyDumper(portName) { Verbose = true };
                Console.WriteLine($"*** Port {portName} geöffnet");
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