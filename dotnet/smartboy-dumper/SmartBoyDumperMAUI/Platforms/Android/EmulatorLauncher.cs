using Android.Content;

namespace SmartBoyDumperMAUI.Platforms.Android
{
    public static class EmulatorLauncher
    {
        private static readonly string[] KnownEmulators =
        {
            "com.fastemulator.gbc",      // My OldBoy! (paid)
            "com.fastemulator.gbcfree",  // My OldBoy! Free
        };

        public static bool TryOpenDirectly(Context context, string? preferredPackage = null)
        {
            var candidates = preferredPackage != null
                ? new[] { preferredPackage }
                : KnownEmulators;

            var pm = context.PackageManager!;

            foreach (var pkg in candidates)
            {
                var intent = pm.GetLaunchIntentForPackage(pkg);
                if (intent != null)
                {
                    intent.AddFlags(ActivityFlags.NewTask);
                    context.StartActivity(intent);
                    return true;
                }
            }

            return false;
        }
    }
}