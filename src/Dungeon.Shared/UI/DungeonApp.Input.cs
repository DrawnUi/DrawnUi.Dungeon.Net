// Keys and the pointer (port of `act`, `key_down`, `key_up` and `gesture` in examples/dungeon/src/lib.rs).

namespace DungeonRun;

public partial class DungeonApp
{
    /// <summary>Points of sideways drag per lane.</summary>
    const float PAN_STEP = 44.0f;
    /// <summary>
    /// A drag that went this far (points) was a drag, not a tap, whatever its release says: the engine's own tap
    /// threshold.
    /// </summary>
    const float TAP_SLOP = 16.0f;
    /// <summary>
    /// A held lane key steps again after this long, then at this pace (the system's own key repeat waits half a
    /// second: too slow for a run).
    /// </summary>
    const float HOLD_FIRST = 0.2f;
    const float HOLD_NEXT = 0.13f;

    /// <summary>A lane change or a jump; on the title, a jump (a tap, Space, Enter) starts the run, a drag or a lane key does not.</summary>
    void Act(bool left, bool right, bool jump)
    {
        if (_paused)
            return;
        switch (_phase)
        {
            case Phase.Attract when jump:
                _scene.World.Start();
                break;
            case Phase.Playing:
                _input.Left |= left;
                _input.Right |= right;
                _input.Jump |= jump;
                break;
            // Any key or tap leaves GAME OVER or the view from outside for the title.
            case Phase.Over when _overReady && _leaving == 0:
                _leaving = 2;
                break;
        }
    }

    public void KeyDown(InputKey key)
    {
        // The canvas's keyboard navigation used it (a button a keyboard user tabbed to took Enter or Space, an arrow
        // moved in a group): not the game's.
        if (KeyboardManager.IsKeyHandled)
            return;
        // GAME OVER takes any key (F1 still opens the help).
        if (_phase == Phase.Over && !_paused && key != InputKey.F1)
        {
            Act(false, false, true);
            return;
        }
        switch (key)
        {
            // In a dialog, Enter and Space press its main button (numpad Enter arrives as Enter, as Rust's NumpadEnter).
            case InputKey.Enter or InputKey.Space when _paused:
                CloseDialog();
                break;
            case InputKey.F1:
                ToggleDialog(HelpDialog);
                break;
            case InputKey.Escape or InputKey.KeyP:
                Menu();
                break;
            case InputKey.ArrowLeft or InputKey.KeyA:
                (_held, _holdWait) = (-1, HOLD_FIRST);
                Act(true, false, false);
                break;
            case InputKey.ArrowRight or InputKey.KeyD:
                (_held, _holdWait) = (1, HOLD_FIRST);
                Act(false, true, false);
                break;
            case InputKey.ArrowUp or InputKey.KeyW or InputKey.Space or InputKey.Enter:
                Act(false, false, true);
                break;
        }
    }

    /// <summary>The held lane key was released (the other one may have taken over since).</summary>
    public void KeyUp(InputKey key)
    {
        if (key is InputKey.ArrowLeft or InputKey.KeyA && _held < 0)
            _held = 0;
        else if (key is InputKey.ArrowRight or InputKey.KeyD && _held > 0)
            _held = 0;
    }

    /// <summary>The pointer, a finger or the mouse alike: a drag steers, a tap jumps.</summary>
    public void Gesture(SkiaGesturesParameters args, float scale)
    {
        scale = Math.Max(scale, 0.1f);
        switch (args.Type)
        {
            case TouchActionResult.Down:
                (_swiped, _panSteps, _touched) = (false, 0, args.Event.Pointer?.DeviceType == PointerDeviceType.Touch);
                break;
            // A drag steers, with a finger or the mouse: a lane for every PAN_STEP points the pointer has gone
            // sideways since the press, there and back.
            case TouchActionResult.Panning:
            {
                // A finger that went past the tap threshold and came back ends within it: the engine reports a
                // Tapped then, but the player dragged, so no jump on this release.
                float dx = args.Event.Distance.Total.X / scale, dy = args.Event.Distance.Total.Y / scale;
                if (Math.Abs(dx) >= TAP_SLOP || Math.Abs(dy) >= TAP_SLOP)
                    _swiped = true;
                int steps = (int)MathF.Truncate(dx / PAN_STEP);
                if (steps != _panSteps)
                {
                    bool right = steps > _panSteps;
                    _panSteps += right ? 1 : -1;
                    _swiped = true;
                    Act(!right, right, false);
                }
                break;
            }
            // A tap or a click that steered nothing is a jump, wherever it lands.
            case TouchActionResult.Tapped when !_swiped:
                Act(false, false, true);
                break;
        }
    }
}
