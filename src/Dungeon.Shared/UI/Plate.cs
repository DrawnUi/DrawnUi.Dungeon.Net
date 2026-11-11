// The plate every button is made of (port of examples/dungeon/src/plate.rs): a SkiaShape whose line is quiet at
// rest and takes the main color, with a glow, under the pointer or a press.

namespace DungeonRun;

public class Plate : SkiaShape
{
    readonly Color _rest;
    readonly Color _lit;
    bool _on;

    /// <summary>A plate stroked with <paramref name="rest"/>, and with <paramref name="lit"/> while hovered or pressed.</summary>
    public Plate(Color rest, Color lit)
    {
        _rest = rest;
        _lit = lit;
        UseCache = SkiaCacheType.Operations;
        StrokeColor = rest;
        ReceivesHover = true;
    }

    void Light(bool on)
    {
        if (on == _on)
            return;
        _on = on;
        StrokeColor = on ? _lit : _rest;
        Shadows = on ? new List<SkiaShadow> { new() { Color = _lit, X = 0, Y = 0, Blur = 12, Opacity = 0.85 } } : new List<SkiaShadow>();
    }

    public override bool OnHover(bool state)
    {
        Light(state);
        return base.OnHover(state);
    }

    public override ISkiaGestureListener ProcessGestures(SkiaGesturesParameters args, GestureEventProcessingInfo apply)
    {
        if (args.Type == TouchActionResult.Down)
            Light(true);
        // A finger leaves; a mouse is still over the button.
        else if (args.Type == TouchActionResult.Up && args.Event.Pointer?.DeviceType == PointerDeviceType.Touch)
            Light(false);
        return base.ProcessGestures(args, apply);
    }
}
