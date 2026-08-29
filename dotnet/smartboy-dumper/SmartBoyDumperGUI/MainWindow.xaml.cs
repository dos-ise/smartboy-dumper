using System;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using SmartboyDumperCs;

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
                MessageBox.Show("Bitte einen COM-Port auswählen.", "Kein Port gewählt",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            LogTextBox.Clear();
            StatusText.Text = "Läuft...";
            StartButton.IsEnabled = false;
            PortComboBox.IsEnabled = false;
            RefreshButton.IsEnabled = false;

            _isRunning = true;

            // Console-Ausgaben der SmartboyDumper-Klasse in die TextBox umleiten
            _originalConsoleOut = Console.Out;
            Console.SetOut(new TextBoxWriter(LogTextBox));

            bool verbose = VerboseCheckBox.Dispatcher.Invoke(() => VerboseCheckBox.IsChecked == true);

            try
            {
                await Task.Run(() =>
                {
                    using var dumper = new SmartboyDumper(portName) { Verbose = verbose };
                    _dumper = dumper;
                    dumper.Run();
                });

                StatusText.Text = "Fertig - ROM gespeichert.";
            }
            catch (SmartboyException ex)
            {
                StatusText.Text = "Fehler.";
                Console.WriteLine($"Fehler: {ex.Message}");
            }
            catch (Exception ex)
            {
                StatusText.Text = "Unerwarteter Fehler.";
                Console.WriteLine($"Unerwarteter Fehler: {ex}");
            }
            finally
            {
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