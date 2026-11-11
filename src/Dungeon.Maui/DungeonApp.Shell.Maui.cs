// The dialogs' host on MAUI, where SkiaShell is a page, not a control: a layer over the game's tree that shows the
// dialogs as the drawn SkiaShell of the other .NET heads shows its popups (centered, no overlay, scaled in from 0.5
// while fading in, a tap outside closes, screen readers and Tab stay inside).

namespace DungeonRun;

public partial class DungeonApp
{
    const uint PopupSpeed = 250; // SkiaShell.PopupsAnimationSpeed

    readonly List<SkiaLayer> _popups = new();
    SkiaLayer _host;

    /// <summary>The game's tree with the layer its dialogs open in.</summary>
    SkiaControl Shell(SkiaControl root) => new SkiaLayer
    {
        HorizontalOptions = LayoutOptions.Fill,
        VerticalOptions = LayoutOptions.Fill,
        Children = new List<SkiaControl> { root },
    }.Assign(out _host);

    /// <summary>Dialogs open now.</summary>
    int PopupsCount => _popups.Count;

    void ShowPopup(SkiaControl content)
    {
        var wrapper = new SkiaLayer
        {
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Scale = 0.5,
            Children = new List<SkiaControl> { content },
        };
        var layer = new SkiaLayer
        {
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            ZIndex = 2000 + _popups.Count,
            BlockGesturesBelow = true,
            AccessibilityModal = true,
            Opacity = 0.1,
            Children = new List<SkiaControl> { wrapper },
        };
        layer.OnTapped((me, e) =>
        {
            var location = e.ProcessingInfo.MappedLocation;
            var offset = e.ProcessingInfo.ChildOffset;
            if (!wrapper.HitIsInside(location.X + offset.X, location.Y + offset.Y))
                Close(layer, true);
        });
        _popups.Add(layer);
        _host.AddSubView(layer);
        _ = layer.FadeToAsync(1, PopupSpeed);
        _ = wrapper.ScaleToAsync(1, 1, PopupSpeed);
    }

    void HidePopup(bool animated)
    {
        if (_popups.Count > 0)
            Close(_popups[^1], animated);
    }

    async void Close(SkiaLayer layer, bool animated)
    {
        if (!_popups.Remove(layer))
            return;
        if (animated)
            await Task.WhenAll(layer.FadeToAsync(0, PopupSpeed), layer.Views[0].ScaleToAsync(0, 0, PopupSpeed));
        _host.RemoveSubView(layer);
        layer.Dispose();
        ShellChanged();
    }
}
