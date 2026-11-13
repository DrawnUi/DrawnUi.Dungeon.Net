using Android.App;
using Android.Content.PM;

namespace DungeonRun;

[Activity(Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Android.OS.Bundle savedInstanceState)
    {
        // the scene cap for tries: adb shell am start -n <package>/<activity> --ef maxdensity 2 (0 = no cap)
        SceneCap.MaxDensity = Intent?.GetFloatExtra("maxdensity", SceneCap.MaxDensity) ?? SceneCap.MaxDensity;
        base.OnCreate(savedInstanceState);
    }
}
