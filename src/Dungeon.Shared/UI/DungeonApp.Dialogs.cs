// Help and pause (port of the dialog half of examples/dungeon/src/lib.rs): SkiaShell popups without an overlay,
// their panel, frame and glow one animated shader, the content over it.

using DrawnUi.Models;

namespace DungeonRun;

public partial class DungeonApp
{
    /// <summary>Points around a dialog's panel that its glow needs (the shader counts with the same numbers).</summary>
    const int DIALOG_GLOW = 26;
    const float DIALOG_WIDTH = 452.0f;

    /// <summary>A dialog over the run stops it.</summary>
    void ShellChanged()
    {
        _paused = _shell.PopupsCount > 0;
        _held = 0;
        _veil.IsVisible = _paused;
        // Under a dialog the HUD's buttons are gone, so Tab stays in the dialog.
        _buttons.IsVisible = !_paused;
    }

    void OpenDialog(Func<SkiaControl> dialog)
    {
        var content = dialog();
        _ = _shell.OpenPopupAsync(content, showOverlay: false);
        // the popup is in the shell's list at once: the run stops now, not after the popup's animation
        ShellChanged();
    }

    void ClosePopup(bool animated)
    {
        _ = _shell.ClosePopupAsync(animated);
    }

    /// <summary>
    /// Escape, P or the pause button: a run pauses (home or resume); the title, which has nothing to pause, shows
    /// how to play; an open dialog closes.
    /// </summary>
    void Menu()
    {
        if (_paused)
            CloseDialog();
        else if (_phase is Phase.Playing or Phase.Countdown)
            OpenDialog(PauseDialog);
        else if (_phase == Phase.Attract)
            OpenDialog(HelpDialog);
    }

    /// <summary>Opens a dialog, or closes the one that is open.</summary>
    void ToggleDialog(Func<SkiaControl> dialog)
    {
        if (_paused)
            ClosePopup(true);
        else
            OpenDialog(dialog);
    }

    /// <summary>The open dialog closes; the help read during a run gives way to the pause again.</summary>
    void CloseDialog()
    {
        bool backToPause = _helpOpen && _phase is Phase.Playing or Phase.Countdown;
        ClosePopup(!backToPause);
        if (backToPause)
            OpenDialog(PauseDialog);
    }

    /// <summary>A dialog: its panel, frame and glow are one animated shader; the content lies over it.</summary>
    SkiaControl Dialog(string title, params SkiaControl[] content)
    {
        // As wide as it likes on a desktop, as wide as the screen lets it on a phone.
        float width = Math.Max(Math.Min(DIALOG_WIDTH, _canvasWidth - 4.0f), 280.0f);
        return new SkiaLayer
        {
            WidthRequest = width,
            HorizontalOptions = LayoutOptions.Center,
            AccessibilityRole = Aria.RoleDialog,
            AccessibilityLabel = title,
            Children = new List<SkiaControl>
            {
                new SkiaLayer
                {
                    VerticalOptions = LayoutOptions.Fill,
                    VisualEffects =
                    {
                        new SkiaShaderEffect
                        {
                            ShaderCode = Shaders.DIALOG,
                            UseBackground = PostRendererEffectUseBackgroud.Never,
                            AutoCreateInputTexture = false,
                        }
                        .SetUniform("uWidth", width)
                        .OnShaderError((me, error) => Console.WriteLine($"dungeon: dialog SkSL: {error}")),
                    },
                }.UpdateNonStop(), // its time runs while it is open
                new SkiaStack
                {
                    Spacing = 9,
                    Margin = new Thickness(DIALOG_GLOW),
                    Padding = new Thickness(_narrow ? 14 : 24),
                    Children = new List<SkiaControl>
                    {
                        // A lit LED bar.
                        new SkiaShape
                        {
                            WidthRequest = 44,
                            HeightRequest = 3,
                            CornerRadius = 1.5,
                            BackgroundColor = C(0xFFFF_E6C0),
                            Shadows = new List<SkiaShadow> { new() { Color = C(ACCENT), X = 0, Y = 0, Blur = 9, Opacity = 1 } },
                            Margin = new Thickness(0, 2, 0, 4),
                        },
                        Line(title, 22, ACCENT).WithMargin(0, 0, 0, 4),
                    }.Concat(content).ToList(),
                },
            },
        };
    }

    /// <summary>A heading inside a dialog.</summary>
    static SkiaLabel Section(string text, uint color) => Line(text, 12, color).WithMargin(0, 10, 0, 0);

    /// <summary>
    /// One thing of the dungeon: how it looks, its name, what to do about it. On a narrow screen the name and the
    /// action do not share a line.
    /// </summary>
    static SkiaControl Thing(bool narrow, SkiaShape swatch, string name, string action, uint color)
    {
        if (narrow)
        {
            return new SkiaStack
            {
                Spacing = 0,
                Children = new List<SkiaControl>
                {
                    ThingRow(swatch, name, null),
                    Line(action, 13, color).WithHorizontalOptions(LayoutOptions.End).WithMargin(0, -4, 0, 0),
                },
            };
        }
        return ThingRow(swatch, name, Line(action, 13, color).WithHorizontalOptions(LayoutOptions.End).WithVerticalOptions(LayoutOptions.Center));
    }

    static SkiaLayer ThingRow(SkiaShape swatch, string name, SkiaControl action)
    {
        var children = new List<SkiaControl>
        {
            new SkiaLayer
            {
                WidthRequest = 26,
                VerticalOptions = LayoutOptions.Fill,
                Children = new List<SkiaControl> { swatch.Center() },
            },
            Line(name, 13, 0xFFFF_FFFF).WithMargin(38, 0, 0, 0).WithVerticalOptions(LayoutOptions.Center),
        };
        if (action != null)
            children.Add(action);
        return new SkiaLayer { HeightRequest = 22, Children = children };
    }

    /// <summary>One control: what to press or do, and what it does.</summary>
    static SkiaLayer Pair(string input, string action) => new()
    {
        HeightRequest = 20,
        Children = new List<SkiaControl>
        {
            Line(input, 13, ACCENT).WithVerticalOptions(LayoutOptions.Center),
            Line(action, 13, 0xFFFF_FFFF).WithHorizontalOptions(LayoutOptions.End).WithVerticalOptions(LayoutOptions.Center),
        },
    };

    static SkiaShape Swatch(double width, double height, uint color, bool glow) => new()
    {
        WidthRequest = width,
        HeightRequest = height,
        BackgroundColor = C(color),
        Shadows = glow ? new List<SkiaShadow> { new() { Color = C(color), X = 0, Y = 0, Blur = 8, Opacity = 1 } } : new List<SkiaShadow>(),
    };

    static SkiaShape Circle(SkiaShape shape)
    {
        shape.Type = ShapeType.Circle;
        return shape;
    }

    /// <summary>The help's link text, underlined under the mouse or when tapped.</summary>
    static TextSpan LinkText(bool underline) => new() { Text = $"MADE WITH DRAWNUI FOR {Framework}", Underline = underline };

    /// <summary>
    /// What to do, what not to do, and only then the controls: keyboard, then touch and mouse. The text scrolls when
    /// the screen is too low for it; the last line and the button always show.
    /// </summary>
    SkiaControl HelpDialog()
    {
        // What the dialog needs around the text: its glow, padding, title, last line and button.
        float room = Math.Max(_canvasHeight - 300.0f, 110.0f);
        bool narrow = _narrow;
        _helpOpen = true;
        return Dialog("HOW TO PLAY",
                new SkiaScroll
                {
                    HorizontalOptions = LayoutOptions.Fill,
                    MaximumHeightRequest = room,
                    Content = new SkiaStack
                    {
                        Spacing = 9,
                        Children = new List<SkiaControl>
                        {
                            Section("YOUR GOAL", GOOD).WithMargin(0),
                            Line("RUN AS FAR AS YOU CAN. THE RUN NEVER STOPS.", 11, 0xD9FF_FFFF),
                            Line("COLLECT WHAT GLOWS ON YOUR WAY.", 11, 0xD9FF_FFFF),
                            Thing(narrow, Circle(Swatch(14, 14, 0xFFC0_60FF, true)), "ORB", "A LITTLE HEALTH", GOOD),
                            Thing(narrow, Circle(Swatch(14, 14, 0xFF4D_FF66, true)), "GREEN CROSS", "RESTORES HEALTH", GOOD),
                            Thing(narrow, Circle(Swatch(14, 14, 0xFF4D_E6FF, true)), "SURGE", "FAST, NOTHING HURTS", GOOD),
                            Thing(narrow, Circle(Swatch(14, 14, 0xFFFF_CC4D, true)), "SHIELD", "10 S: ONE PILLAR OR GHOST", GOOD),
                            Section("DO NOT TOUCH", HOT),
                            Line("THESE DRAIN YOUR HEALTH. ZERO ENDS THE RUN.", 11, 0xD9FF_FFFF),
                            Thing(narrow, Swatch(10, 18, 0xFF3A_3D4A, false).Adapt(me =>
                            {
                                me.StrokeColor = C(0xFFFF_8A30);
                                me.StrokeWidth = 1.55;
                            }), "PILLAR", "MOVE TO A FREE LANE", HOT),
                            Thing(narrow, Swatch(22, 4, 0xFFFF_4030, true), "RED BEAM", "JUMP OVER IT", HOT),
                            Thing(narrow, Swatch(22, 9, 0xFFE8_3A0A, true), "LAVA", "JUMP OVER IT", HOT),
                            Thing(narrow, Swatch(14, 18, 0xFFC8_D8FF, true), "GHOST", "CHANGE LANE. IT HURTS MOST", HOT),
                            Controls(),
                        },
                    },
                },
                // A link to DrawnUI, opened in the browser (a new tab on the web): in the link's blue, lighter and
                // underlined under the mouse or when tapped.
                new SkiaLabel
                {
                    FontSize = 11,
                    TextColor = C(LINK),
                    Spans = { LinkText(false) },
                    Margin = new Thickness(0, 12, 0, 0),
                    AccessibilityRole = Aria.RoleLink,
                }
                .OnHovered((me, on) =>
                {
                    me.TextColor = C(on ? LINK_HOVER : LINK);
                    me.Spans.Clear();
                    me.Spans.Add(LinkText(on));
                })
                .OnTapped(me =>
                {
                    me.TextColor = C(LINK_HOVER);
                    me.Spans.Clear();
                    me.Spans.Add(LinkText(true));
#if !DRAWNUI_NET || WEB // the desktop .NET heads (WPF, OpenTK) have no Super.OpenLink yet
                    Super.OpenLink(FrameworkDocs);
#endif
                }),
                Button("OK").WithHorizontalOptions(LayoutOptions.End).WithMargin(0, 6, 0, 0).OnTapped(me => CloseDialog()));
    }

    /// <summary>The controls section of the help: keyboard, then touch and mouse.</summary>
    static SkiaControl Controls() => new SkiaStack
    {
        Spacing = 9,
        Children = new List<SkiaControl>
        {
            Section("KEYBOARD", ACCENT),
            Pair("← →", "CHANGE LANE"),
            Pair("↑ / SPACE", "JUMP"),
            Section("TOUCH OR MOUSE", ACCENT),
            Pair("DRAG LEFT OR RIGHT", "CHANGE LANE"),
            Pair("TAP OR CLICK", "JUMP"),
        },
    };

    SkiaControl PauseDialog()
    {
        _helpOpen = false;
        // A wrap: on a narrow screen what does not fit goes to the next line, nothing squeezed.
        return Dialog("PAUSED", new SkiaWrap
        {
            Spacing = 14,
            HorizontalOptions = LayoutOptions.Center,
            Margin = new Thickness(0, 12, 0, 0),
            Children = new List<SkiaControl>
            {
                GhostButton("HELP").OnTapped(me =>
                {
                    ClosePopup(false);
                    OpenDialog(HelpDialog);
                }),
                // Leaves the run for the title.
                GhostButton("HOME").OnTapped(me =>
                {
                    ClosePopup(true);
                    _scene.World.ToTitle();
                }),
                Button("RESUME").OnTapped(me => ClosePopup(true)),
            },
        });
    }
}
