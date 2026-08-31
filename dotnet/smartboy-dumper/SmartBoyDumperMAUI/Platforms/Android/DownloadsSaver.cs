using Android.Content;
using Android.Provider;
using Uri = Android.Net.Uri;

namespace SmartBoyDumperMAUI.Platforms.Android
{
    public static class DownloadsSaver
    {
        public static (string, Uri itemUri) SaveToDownloads(
            Context context, string sourcePath, string mimeType = "application/octet-stream")
        {
            var fileName = Path.GetFileName(sourcePath);
            var resolver = context.ContentResolver!;

            var values = new ContentValues();
            values.Put(MediaStore.MediaColumns.DisplayName, fileName);
            values.Put(MediaStore.MediaColumns.MimeType, mimeType);
            values.Put(MediaStore.MediaColumns.RelativePath, global::Android.OS.Environment.DirectoryDownloads);

            var collection = MediaStore.Downloads.GetContentUri(MediaStore.VolumeExternal)!;
            var itemUri = resolver.Insert(collection, values);

            using (var output = resolver.OpenOutputStream(itemUri))
            using (var input = File.OpenRead(sourcePath))
            {
                input.CopyTo(output!);
            }

            return ($"Downloads/{fileName}", itemUri);
        }
    }
}