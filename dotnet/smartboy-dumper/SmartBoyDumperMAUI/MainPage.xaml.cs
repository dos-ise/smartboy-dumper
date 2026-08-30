using Android.Content;
using Android.Hardware.Usb;
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
                ShowError("No Smartboy adapter found, or access was denied.");
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

            _dumper.DumpCompleted += (_, path) => MainThread.BeginInvokeOnMainThread(async () =>
            {
                SetState(UiState.Success, filePath: path);
                await PromptSaveAsAsync(path);
            });

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
                MainThread.BeginInvokeOnMainThread(() => ShowError($"Unexpected error: {ex.GetType().Name}: {ex.Message}"));
            }
        }

        private void SetState(UiState state, string? romName = null, int romSizeBytes = 0, string? filePath = null)
        {
            _state = state;

            switch (state)
            {
                case UiState.Idle:
                    StatusIcon.Text = "🎮";
                    StatusTitle.Text = "Ready";
                    StatusSubtitle.Text = "Connect the adapter and start.";
                    ActionButton.Text = "Start Dump";
                    ActionButton.BackgroundColor = (Color)Resources["PrimaryColor"];
                    ProgressSection.IsVisible = false;
                    ResultSection.IsVisible = false;
                    break;

                case UiState.Running:
                    StatusIcon.Text = "🔌";
                    StatusTitle.Text = "Connected";
                    StatusSubtitle.Text = "Waiting for device status...";
                    ActionButton.Text = "Cancel";
                    ActionButton.BackgroundColor = Colors.Gray;
                    break;

                case UiState.WaitingForCartridge:
                    StatusIcon.Text = "📥";
                    StatusTitle.Text = "Ready to Read";
                    StatusSubtitle.Text = "Please insert a cartridge.";
                    ActionButton.Text = "Cancel";
                    break;

                case UiState.CartridgeDetected:
                    StatusIcon.Text = "🧩";
                    StatusTitle.Text = romName ?? "Cartridge Detected";
                    StatusSubtitle.Text = "Determining ROM size...";
                    ActionButton.Text = "Cancel";
                    break;

                case UiState.Dumping:
                    StatusIcon.Text = "💾";
                    StatusTitle.Text = "Reading Cartridge";
                    StatusSubtitle.Text = $"Transferring {romSizeBytes / 1024} KB";
                    ProgressSection.IsVisible = true;
                    DumpProgressBar.Progress = 0;
                    ProgressLabel.Text = "0%";
                    ActionButton.Text = "Cancel";
                    break;

                case UiState.Success:
                    _lastResultPath = filePath;
                    StatusIcon.Text = "✅";
                    StatusTitle.Text = "Done!";
                    StatusSubtitle.Text = "The ROM file has been saved successfully.";
                    ProgressSection.IsVisible = false;
                    ResultSection.IsVisible = true;
                    ResultPathLabel.Text = filePath;
                    ActionButton.Text = "New Dump";
                    ActionButton.BackgroundColor = (Color)Resources["PrimaryColor"];
                    _dumper?.Dispose();
                    _dumper = null;
                    break;

                case UiState.Error:
                    ActionButton.Text = "Try Again";
                    ActionButton.BackgroundColor = (Color)Resources["PrimaryColor"];
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

        private async void SaveAsButton_Clicked(object sender, EventArgs e)
        {
            await PromptSaveAsAsync(_lastResultPath);
        }

        private async Task PromptSaveAsAsync(string? sourcePath)
        {
            if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
                return;

            try
            {
                //using var stream = File.OpenRead(sourcePath);
                //var fileName = Path.GetFileName(sourcePath);

                //var result = await FileSaver.Default.SaveAsync(fileName, stream, CancellationToken.None);

                //if (result.IsSuccessful)
                //{
                //    await DisplayAlert("Saved", $"File saved to:\n{result.FilePath}", "OK");
                //}
                //else if (result.Exception != null)
                //{
                //    ShowError($"Save failed: {result.Exception.Message}");
                //}
                // User cancelled the picker - not an error, do nothing.
            }
            catch (Exception ex)
            {
                ShowError($"Save failed: {ex.Message}");
            }
        }

        private async void ShowLastCrash_Clicked(object sender, EventArgs e)
        {
            var path = Path.Combine(FileSystem.Current.AppDataDirectory, "crash.txt");
            var text = File.Exists(path) ? File.ReadAllText(path) : "No crash log available.";
            await DisplayAlert("Last Crash", text, "OK");
        }

        protected override void OnDisappearing()
        {
            _dumper?.Dispose();
            base.OnDisappearing();
        }
    }
}