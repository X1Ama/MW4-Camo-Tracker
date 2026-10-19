using Android.App;
using Android.Content;
using Android.Media;
using AndroidX.Core.Content;

namespace MasteryTracker.Android;

/// <summary>The two things only Android can do for the shared code: play a sound and install a downloaded update.</summary>
static class PhoneServices
{
    public static void Register()
    {
        PlatformHooks.PlayChime = PlayChime;
        PlatformHooks.InstallPackage = InstallPackage;
    }

    static void PlayChime()
    {
        try
        {
            var ctx = Application.Context;
            var uri = RingtoneManager.GetDefaultUri(RingtoneType.Notification);
            RingtoneManager.GetRingtone(ctx, uri)?.Play();      // the phone's own notification sound
        }
        catch { /* silent phone or no sound service: the banner still shows */ }
    }

    static void InstallPackage(string path)
    {
        try
        {
            var ctx = Application.Context;
            var file = new Java.IO.File(path);
            var uri = FileProvider.GetUriForFile(ctx, ctx.PackageName + ".fileprovider", file);
            var intent = new Intent(Intent.ActionView);
            intent.SetDataAndType(uri, "application/vnd.android.package-archive");
            intent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.NewTask);
            ctx.StartActivity(intent);
        }
        catch (Exception ex)
        {
            SessionStore.Log("could not open the installer: " + ex.Message);
        }
    }
}