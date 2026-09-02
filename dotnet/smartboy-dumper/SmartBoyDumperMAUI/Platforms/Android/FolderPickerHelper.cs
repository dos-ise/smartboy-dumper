using System.Threading.Tasks;
using Android.App;
using Android.Content;

namespace SmartBoyDumperMAUI.Platforms.Android
{
    public static class FolderPickerHelper
    {
        public const int RequestCode = 4242;
        private static TaskCompletionSource<global::Android.Net.Uri?>? _tcs;

        public static Task<global::Android.Net.Uri?> PickFolderAsync(Activity activity)
        {
            _tcs = new TaskCompletionSource<global::Android.Net.Uri?>();

            var intent = new Intent(Intent.ActionOpenDocumentTree);
            activity.StartActivityForResult(intent, RequestCode);

            return _tcs.Task;
        }

        public static void HandleResult(int requestCode, Result resultCode, Intent? data)
        {
            if (requestCode != RequestCode) return;

            if (resultCode == Result.Ok && data?.Data != null)
                _tcs?.TrySetResult(data.Data);
            else
                _tcs?.TrySetResult(null);
        }
    }
}