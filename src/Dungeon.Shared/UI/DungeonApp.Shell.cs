// The dialogs' host on the DrawnUI for .NET heads (web, Blazor, OpenTK, WPF): a drawn SkiaShell whose popups are the
// dialogs. The MAUI head has its own (src/Dungeon.Maui/DungeonApp.Shell.Maui.cs): MAUI's SkiaShell is a page.

namespace DungeonRun;

public partial class DungeonApp
{
    SkiaShell _shell;

    /// <summary>The game's tree inside the shell its dialogs open over.</summary>
    SkiaControl Shell(SkiaControl root) => new SkiaShell
    {
        HorizontalOptions = LayoutOptions.Fill,
        VerticalOptions = LayoutOptions.Fill,
        RootContent = root,
    }.Assign(out _shell).Adapt(me => me.Navigated += (s, e) => ShellChanged());

    /// <summary>Dialogs open now.</summary>
    int PopupsCount => _shell.PopupsCount;

    void ShowPopup(SkiaControl content) => _ = _shell.OpenPopupAsync(content, showOverlay: false);

    void HidePopup(bool animated) => _ = _shell.ClosePopupAsync(animated);
}
