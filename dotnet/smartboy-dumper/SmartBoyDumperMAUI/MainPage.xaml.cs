using System;
using System.IO;
using System.Threading.Tasks;
using Android.Content;
using Android.Hardware.Usb;
using Microsoft.Maui.Storage;
using SmartBoyDumperMAUI.Platforms.Android;

namespace SmartBoyDumperMAUI
{
    public partial class MainPage : ContentPage
    {
        private enum UiState { Idle, Running, WaitingForCartridge, CartridgeDetected, Dumping, Success, Error }

        private SmartboyDumper? _dumper;
        private string? _lastResultPath;
        private UiState _state = UiState.Idle;

        public MainPage()
        {
            InitializeComponent();
        }

        private async void ActionButton_Clicked(object sender, EventArgs e)
        {
            if (_state is UiState.Running or UiState.WaitingForCartridge or
                UiState.CartridgeDetected or UiState.Dumping)
            {
                _dumper?.Dispose();
                _dumper = null;
                SetState(UiState.Idle);
                return;
            }

            ErrorCard.IsVisible = false;
            ResultSection.IsVisible = false;
            ProgressSection.IsVisible = false;

            var context = global::Android.App.Application.Context;
            var device = await UsbPermissionHelper.FindAndRequestDeviceAsync(context);

            if (device == null)
            {
                ShowError("Kein Smartboy-Adapter gefunden oder Zugriff verweigert.");
                return;
            }

            var manager = (UsbManager)context.GetSystemService(Context.UsbService)!;

            IByteTransport transport;
            try
            {
                transport = new AndroidUsbSerialTransport(manager, device);
            }
            catch (SmartboyException ex)
            {
                ShowError(ex.Message);
                return;
            }

            var outputDir = Path.Combine(FileSystem.Current.AppDataDirectory, "Dumps");
            _dumper = new SmartboyDumper(transport, outputDir);

            _dumper.CartridgeAwaited += (_, __) => MainThread.BeginInvokeOnMainThread(() =>
                SetState(UiState.WaitingForCartridge));

            _dumper.RomNameDetected += (_, name) => MainThread.BeginInvokeOnMainThread(() =>
                SetState(UiState.CartridgeDetected, romName: name));

            _dumper.RomSizeDetected += (_, sizeBytes) => MainThread.BeginInvokeOnMainThread(() =>
                SetState(UiState.Dumping, romSizeBytes: sizeBytes));

            _dumper.DumpProgressChanged += (_, percent) => MainThread.BeginInvokeOnMainThread(() =>
                UpdateProgress(percent));

            _dumper.DumpCompleted += (_, path) => MainThread.BeginInvokeOnMainThread(() =>
                SetState(UiState.Success, filePath: path));

            SetState(UiState.Running);

            try
            {
                await Task.Run(() => _dumper!.Run());
            }
            catch (SmartboyException ex)
            {
                MainThread.BeginInvokeOnMainThread(() => ShowError(ex.Message));
            }
            catch (Exception ex)
            {
                MainThread.BeginInvokeOnMainThread(() => ShowError($"Unerwartet: {ex.GetType().Name}: {ex.Message}"));
            }
        }

        private void SetState(UiState state, string? romName = null, int romSizeBytes = 0, string? filePath = null)
        {
            _state = state;

            switch (state)
            {
                case UiState.Idle:
                    StatusIcon.Text = "🎮";
                    StatusTitle.Text = "Bereit";
                    StatusSubtitle.Text = "Adapter anschließen und starten.";
                    ActionButton.Text = "Dump starten";
                    //ActionButton.BackgroundColor = (Color)Application.Current!.Resources["PrimaryColor"];
                    ProgressSection.IsVisible = false;
                    ResultSection.IsVisible = false;
                    break;

                case UiState.Running:
                    StatusIcon.Text = "🔌";
                    StatusTitle.Text = "Verbunden";
                    StatusSubtitle.Text = "Warte auf Statusmeldung des Geräts...";
                    ActionButton.Text = "Abbrechen";
                    ActionButton.BackgroundColor = Colors.Gray;
                    break;

                case UiState.WaitingForCartridge:
                    StatusIcon.Text = "📥";
                    StatusTitle.Text = "Bereit zum Einlesen";
                    StatusSubtitle.Text = "Bitte eine Cartridge einlegen.";
                    ActionButton.Text = "Abbrechen";
                    break;

                case UiState.CartridgeDetected:
                    StatusIcon.Text = "🧩";
                    StatusTitle.Text = romName ?? "Cartridge erkannt";
                    StatusSubtitle.Text = "Ermittle ROM-Größe...";
                    ActionButton.Text = "Abbrechen";
                    break;

                case UiState.Dumping:
                    StatusIcon.Text = "💾";
                    StatusTitle.Text = "Lese Cartridge aus";
                    StatusSubtitle.Text = $"{romSizeBytes / 1024} KB werden übertragen";
                    ProgressSection.IsVisible = true;
                    DumpProgressBar.Progress = 0;
                    ProgressLabel.Text = "0%";
                    ActionButton.Text = "Abbrechen";
                    break;

                case UiState.Success:
                    _lastResultPath = filePath;
                    StatusIcon.Text = "✅";
                    StatusTitle.Text = "Fertig!";
                    StatusSubtitle.Text = "Die ROM-Datei wurde erfolgreich gespeichert.";
                    ProgressSection.IsVisible = false;
                    ResultSection.IsVisible = true;
                    ResultPathLabel.Text = filePath;
                    ActionButton.Text = "Neuer Dump";
                    //ActionButton.BackgroundColor = (Color)Application.Current!.Resources["PrimaryColor"];
                    _dumper?.Dispose();
                    _dumper = null;
                    break;

                case UiState.Error:
                    ActionButton.Text = "Erneut versuchen";
                    //ActionButton.BackgroundColor = (Color)Application.Current!.Resources["PrimaryColor"];
                    _dumper?.Dispose();
                    _dumper = null;
                    break;
            }
        }

        private void UpdateProgress(int percent)
        {
            DumpProgressBar.Progress = percent / 100.0;
            ProgressLabel.Text = $"{percent}%";
        }

        private void ShowError(string message)
        {
            ErrorLabel.Text = message;
            ErrorCard.IsVisible = true;
            SetState(UiState.Error);
        }

        private async void OpenFileButton_Clicked(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_lastResultPath) || !File.Exists(_lastResultPath))
                return;

            try
            {
                await Launcher.Default.OpenAsync(new OpenFileRequest(
                    "ROM-Datei", new ReadOnlyFile(_lastResultPath)));
            }
            catch (Exception ex)
            {
                ShowError($"Konnte Datei nicht öffnen: {ex.Message}");
            }
        }

        private async void ShowLastCrash_Clicked(object sender, EventArgs e)
        {
            var path = Path.Combine(FileSystem.Current.AppDataDirectory, "crash.txt");
            var text = File.Exists(path) ? File.ReadAllText(path) : "Kein Crash-Log vorhanden.";
            await DisplayAlert("Letzter Crash", text, "OK");
        }

        protected override void OnDisappearing()
        {
            _dumper?.Dispose();
            base.OnDisappearing();
        }
    }
}