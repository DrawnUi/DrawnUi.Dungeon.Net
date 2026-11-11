global using DrawnUi;
global using DrawnUi.Draw;
global using DrawnUi.Views;
global using DrawnUi.Controls;
global using DrawnUi.Gaming;
global using DrawnUi.Infrastructure.Enums;
global using AppoMobi.Gestures;
global using SkiaSharp;
using System.Runtime.Versioning;
using DrawnUi.Models;
using DrawnUi.OpenTk;
using DungeonRun;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using Color = DrawnUi.Color;

// Dungeon Run on the desktop: an OpenTK window (Windows, Linux) on DrawnUi.OpenTk, the window of the Rust game's size.

// Every label is text to a screen reader.
SkiaLabel.DefaultAccessibilityRole = Aria.RoleText;
// The title and the help name the framework: "DRAWNUI FOR OPENTK SAMPLE".
DungeonApp.Framework = "OPENTK";

Super.UseDrawnUi()
    .ConfigureFonts(fonts =>
    {
        fonts.AddFont("fonts/Orbitron-Regular.ttf", "FontGame");
        fonts.AddFont("fonts/Orbitron-Black.ttf", "FontScore");
        fonts.AddFont("fonts/NotoSansMathSymbols-Subset.ttf", DungeonApp.SYMBOLS);
    })
    .ConfigureStyles(styles =>
    {
        // The first font registered is the default in DrawnUI for Rust: every label is FontGame.
        styles.AddStyle(new Style
        {
            TargetType = typeof(SkiaLabel),
            ApplyToDerivedTypes = true,
            Setters = { new Setter { Property = SkiaLabel.FontFamilyProperty, Value = "FontGame" } },
        });
    })
    .Build();

var canvas = new Canvas
{
    RenderingMode = RenderingModeType.Accelerated,
    // a game: something moves every frame
    UpdateMode = UpdateModeType.Constant,
    BackgroundColor = Colors.Black,
    HorizontalOptions = LayoutOptions.Fill,
    VerticalOptions = LayoutOptions.Fill,
    Content = new DungeonApp().Build(),
};

var window = new NativeWindowSettings
{
    ClientSize = new Vector2i(1100, 700),
    Title = "Dungeon Run",
    API = ContextAPI.OpenGL,
    APIVersion = OperatingSystem.IsLinux() ? new Version(3, 3) : new Version(4, 6),
    Profile = ContextProfile.Core,
    WindowState = WindowState.Normal,
    Icon = DungeonWindow.LoadIcon(),
};

using (var app = new DungeonWindow(new GameWindowSettings(), window, canvas))
    app.Run();

/// <summary>The game's window: dark title bar, keys to the game.</summary>
class DungeonWindow(GameWindowSettings game, NativeWindowSettings native, Canvas canvas) : DrawnUiWindow(game, native, canvas)
{
    [SupportedOSPlatform("windows")]
    protected override void ConfigureWindowChrome(IntPtr hwnd)
    {
        WindowChrome.SetCaptionColor(hwnd, 0, 0, 0);
        WindowChrome.SetBorderColor(hwnd, 0, 0, 0);
    }

    protected override void OnKeyDown(KeyboardKeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (OpenTkKeyMapper.Map(e.Key) is { } key)
            KeyboardManager.KeyboardPressed(key);
    }

    protected override void OnKeyUp(KeyboardKeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (OpenTkKeyMapper.Map(e.Key) is { } key)
            KeyboardManager.KeyboardReleased(key);
    }

    /// <summary>The window's own icon: the exe's icon.ico (embedded), 32x32 RGBA as GLFW takes it.</summary>
    public static OpenTK.Windowing.Common.Input.WindowIcon LoadIcon()
    {
        var assembly = typeof(DungeonWindow).Assembly;
        var name = assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("icon.ico", StringComparison.OrdinalIgnoreCase));
        if (name == null)
            return null;
        using var stream = assembly.GetManifestResourceStream(name);
        using var decoded = SKBitmap.Decode(stream);
        if (decoded == null)
            return null;
        using var icon = decoded.Resize(new SKImageInfo(32, 32, SKColorType.Rgba8888, SKAlphaType.Unpremul),
            new SKSamplingOptions(SKCubicResampler.Mitchell));
        return new OpenTK.Windowing.Common.Input.WindowIcon(new OpenTK.Windowing.Common.Input.Image(32, 32, icon.Bytes));
    }
}
