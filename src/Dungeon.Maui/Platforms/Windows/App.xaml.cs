using Microsoft.UI.Xaml;

namespace DungeonRun.WinUI;

/// <summary>The WinUI application that starts the MAUI app (Windows, unpackaged).</summary>
public partial class App : MauiWinUIApplication
{
    public App()
    {
        this.InitializeComponent();
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}