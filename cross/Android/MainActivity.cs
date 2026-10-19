using Android.App;
using Android.Content.PM;
using Avalonia;
using Avalonia.Android;

namespace MasteryTracker.Android;

[Activity(
    Label = "MW4 Camo Tracker",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@mipmap/ic_launcher",
    MainLauncher = true,
    Exported = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : AvaloniaMainActivity<App>
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        PhoneServices.Register();          // the chime and the update installer
        return base.CustomizeAppBuilder(builder).WithInterFont();
    }
}