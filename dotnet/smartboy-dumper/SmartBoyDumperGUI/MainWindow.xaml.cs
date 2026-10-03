using System;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using smartboy_dumper;

namespace SmartBoyDumperGUI
{
    public partial class MainWindow : Window
    {
        private SmartboyDumper? _dumper;
        private TextWriter? _originalConsoleOut;
        private bool _isRunning;

        public MainWindow()
        {
            InitializeComponent();
            RefreshPorts();
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e) => RefreshPorts();

        private void RefreshPorts()
        {
            var selected = PortComboBox.SelectedItem as string;

            var ports = SerialPort.GetPortNames()
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();

            PortComboBox.ItemsSource = ports;

            if (selected != null && ports.Contains(selected))
                PortComboBox.SelectedItem = selected;
            else if (ports.Count > 0)
                PortComboBox.SelectedIndex = 0;
        }

        private async void StartButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunning)
                return;

            if (PortComboBox.SelectedItem is not string portName)
            {
                MessageBox.Show("Please select a COM port.", "No port selected",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            LogTextBox.Clear();
            StatusText.Text = "Running...";
            StartButton.IsEnabled = false;
            PortComboBox.IsEnabled = false;
            RefreshButton.IsEnabled = false;

            _isRunning = true;

            // Console-Ausgaben der SmartboyDumper-Klasse in die TextBox umleiten
            _originalConsoleOut = Console.Out;
            var writer = new TextBoxWriter(LogTextBox);
            Console.SetOut(writer);

            try
            {
                await Task.Run(() =>
                {
                    // Transport erzeugen
                    using var transport = new SerialPortByteTransport(portName);

                    // Output-Verzeichnis festlegen
                    string outputDir = Path.Combine(
                        AppDomain.CurrentDomain.BaseDirectory, "dumps");

                    // Dumper erzeugen
                    using var dumper = new SmartboyDumper(transport, outputDir);
                    _dumper = dumper;

                    // Events abonnieren
                    dumper.RomNameDetected += (_, name) =>
                        Console.WriteLine($"ROM name: {name}");

                    dumper.RomSizeDetected += (_, size) =>
                        Console.WriteLine($"ROM size: {size} bytes");

                    dumper.DumpProgressChanged += (_, percent) =>
                        Console.WriteLine($"Dump: {percent}%");

                    dumper.DumpCompleted += (_, file) =>
                        Console.WriteLine($"Dump completed: {file}");

                    dumper.LogMessage += (_, msg) => Console.WriteLine(msg);
                    dumper.SaveCompleted += (_, file) =>
                        Console.WriteLine($"Save game saved: {file}");

                    dumper.CartridgeAwaited += (_, __) =>
                        Console.WriteLine("Please insert a cartridge!");

                    // Starten
                    dumper.Run();
                });

                StatusText.Text = "Done - ROM saved.";
            }
            catch (SmartboyException ex)
            {
                StatusText.Text = "Error.";
                Console.WriteLine($"Error: {ex.Message}");
            }
            catch (Exception ex)
            {
                StatusText.Text = "Unexpected error.";
                Console.WriteLine($"Unerwarteter Error: {ex}");
            }
            finally
            {
                writer.StopAndFlush();
                Console.SetOut(_originalConsoleOut);
                _dumper = null;
                _isRunning = false;

                StartButton.IsEnabled = true;
                PortComboBox.IsEnabled = true;
                RefreshButton.IsEnabled = true;
            }
        }


        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            _dumper?.Dispose();
            base.OnClosing(e);
        }
    }
}