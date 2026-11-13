// One frame (port of `tick`, `show_prompt` and `flash_score` in examples/dungeon/src/lib.rs): the world moves, the
// scene draws again, the HUD follows. Labels and effects are touched only when what they show changes.

namespace DungeonRun;

/// <summary>What the big prompt says, in its own color: a lesson or a power-up's name.</summary>
public enum PromptKind
{
    Jump,
    Lane,
    Health,
    Surge,
    Shield,
    Absorbed,
    Cheer,
    /// <summary>The one-time lessons: after the first hit, after the first cell lost to the drain, after the first ghost gone by.</summary>
    Heal,
    Drain,
    Avoid,
    /// <summary>Under four cells of health.</summary>
    Low,
    /// <summary>The time of an escape, in tenths of a second.</summary>
    Time,
}

/// <summary>A prompt and what it carries (a cheer's words, an escape's time).</summary>
public readonly record struct Prompt(PromptKind Kind, string Cheer = null, uint Tenths = 0);

public partial class DungeonApp
{
    /// <summary>Said after every stretch of running without a hit.</summary>
    static readonly string[] CHEERS = ["WELL DONE!", "NICE RUN!", "YOU ARE KILLING IT!", "UNSTOPPABLE!", "ON FIRE!", "FLAWLESS!", "KEEP GOING!", "SMOOTH!"];
    /// <summary>Seconds of clean running between two cheers.</summary>
    const float CHEER_EVERY = 12.0f;

    /// <summary>The score jumps in a violet glow for a moment: the glow shader is on it only that long.</summary>
    void FlashScore()
    {
        _pop = 1.0f;
        if (!_scoreBox.VisualEffects.Contains(_scoreGlow))
        {
            _uScoreGlow[0] = 1.0f;
            _scoreGlow.SetUniform("uGlow", _uScoreGlow);
            _scoreBox.VisualEffects.Add(_scoreGlow);
        }
    }

    /// <summary>One frame: the world moves, the scene draws again, the HUD follows.</summary>

    public void Tick(float delta)
    {
        float sceneScale = SceneCap.For(_game.RenderingScale);
        if (sceneScale != _appliedScale)
            ApplyRenderScale(sceneScale);

        // On a narrow screen (a phone held upright) the health does not fit between the score and the buttons:
        // it goes under them.
        // the size the system bars and cutouts leave free (the whole canvas on the web and the desktop)
        float canvasWidth = (float)(_game.Width - _safe.Margin.HorizontalThickness);
        float canvasHeight = (float)(_game.Height - _safe.Margin.VerticalThickness);
        _canvasHeight = canvasHeight;
        if (canvasWidth != _canvasWidth)
        {
            _canvasWidth = canvasWidth;
            // The big texts fit the width they have.
            _name.FontSize = canvasWidth < 440.0f ? 32.0 : 44.0;
            _count.WidthRequest = Math.Min(canvasWidth, 760.0f);
            _promptBox.WidthRequest = Math.Min(canvasWidth, 760.0f);
            _prompt.FontSize = canvasWidth < 520.0f ? 28.0 : 44.0;
        }
        bool narrow = canvasWidth < NARROW;
        if (narrow != _narrow)
        {
            _narrow = narrow;
            _hud.Margin = new Thickness(0, narrow ? 62 : 16, 0, 0);
        }
        if (_paused)
            return;
        if (_held != 0)
        {
            _holdWait -= delta;
            if (_holdWait <= 0.0f)
            {
                _holdWait = HOLD_NEXT;
                (_input.Left, _input.Right) = (_held < 0, _held > 0);
            }
        }
        var world = _scene.World;
        // A frame that came late (a hidden tab) is not a jump through the walls.
        world.Step(Math.Min(delta, 0.05f), ref _input);
        // Leaving GAME OVER: this frame the curtain shows and takes its picture of it (the scene stays live under
        // GAME OVER until then), the next one goes to the title under the burning curtain.
        bool closing = false;
        if (_leaving > 0)
        {
            _leaving -= 1;
            if (_leaving > 0)
                closing = true;
            else
                world.ToTitle();
        }
        _overReady = world.OverFor > 0.6f;
        var phase = world.Phase;
        uint orbs = world.Orbs, hits = world.Hits, ghostsPassed = world.GhostsPassed;
        float health = world.Health, runTime = world.RunTime, worldGhost = world.Ghost, burn = world.Burn;
        var hint = world.GetHint();
        bool young = world.Young, escaped = world.Escaped;
        float? bestTime = world.BestTime;
        long? worldKiller = world.Killer;
        uint meters = world.Distance / 10 * 10;
        uint? count = world.Count;
        byte? pickup = world.TakePickup();
        _uFx[0] = world.Rush;
        _uFx[1] = world.Warp;
        _uFx[2] = world.Flash;
        _uFx[3] = world.Time;
        Span<float> fx2 = stackalloc float[7];
        world.Light(fx2.Slice(2, 3));
        // The ghost's blackout, plus 2 while its touch is felt (the post effect's haunt).
        float haunt = world.Ghost > 0.0f || world.Killer != null ? 2.0f : 0.0f;
        (fx2[0], fx2[1], fx2[5], fx2[6]) = (world.Heal, world.SurgeGlow, world.OrbPulse, world.Ghost + haunt);
        Span<float> orb = stackalloc float[3];
        world.OrbFlash(orb);
        // The aura fades over the shield's last two seconds.
        float shield0 = Math.Min(world.Shield / 2.0f, 1.0f), shield1 = world.ShieldBurst;
        uint streak = world.Streak;

        _post.SetUniform("uFx", _uFx);
        if (!fx2.SequenceEqual(_shownFx2))
        {
            fx2.CopyTo(_shownFx2);
            (_uFx2[0], _uFx2[1], _uFx2[2], _uFx2[3]) = (fx2[0], fx2[1], fx2[5], fx2[6]);
            _post.SetUniform("uFx2", _uFx2);
            fx2.Slice(2, 3).CopyTo(_uRay);
            _post.SetUniform("uRay", _uRay);
        }
        if (shield0 != _shownShield[0] || shield1 != _shownShield[1])
        {
            (_shownShield[0], _shownShield[1]) = (shield0, shield1);
            (_uShield[0], _uShield[1]) = (shield0, shield1);
            _post.SetUniform("uShield", _uShield);
        }
        if (!orb.SequenceEqual(_shownOrb))
        {
            orb.CopyTo(_shownOrb);
            orb.CopyTo(_uOrb);
            _post.SetUniform("uOrb", _uOrb);
        }
        _scene.Update();
        // The shield took a hit: ABSORBED!
        if (shield1 > _lastBurst && phase == Phase.Playing)
        {
            (_notice, _hint) = (1.3f, Hint.None);
            ShowPrompt(new Prompt(PromptKind.Absorbed));
        }
        _lastBurst = shield1;

        if (phase != _phase)
        {
            if (_phase == Phase.Countdown && phase == Phase.Playing)
                _go = 0.7f;
            if (phase == Phase.Countdown)
            {
                // A new run: the readouts start over.
                (_shownScore, _shownDistance) = (0, 0);
                _clean = 0.0f;
                _score.Text = "0 M";
            }
            _phase = phase;
            bool onTitle = phase == Phase.Attract;
            _title.IsVisible = onTitle;
            if (onTitle)
            {
                (_notice, _hint, _pending) = (0.0f, Hint.None, null);
                ShowPrompt(null);
                _best.IsVisible = bestTime != null;
                if (bestTime is float time)
                    _best.Text = $"BEST ESCAPE {FormatTime((uint)(time * 10.0f))}";
            }
            // Rust hides it with opacity 0; here a control at opacity 0 still draws, and the frame rate label lays out
            // its changing text every frame: off the screen during a run it is not drawn at all.
            _fps.IsVisible = onTitle;
            double opacity = onTitle ? 0.0 : 1.0;
            _hud.Opacity = opacity;
            _scoreBox.Opacity = opacity;
        }
        // The curtain: shown the frame GAME OVER is left, it keeps that picture (its shader takes its texture once),
        // then burns it away over the title's run.
        bool curtainOn = closing || burn > 0.0f;
        float hole = closing ? 0.0f : 1.0f - burn;
        if (curtainOn != _curtainOn)
        {
            _curtainOn = curtainOn;
            _curtain.IsVisible = curtainOn;
            if (!curtainOn)
            {
                _curtainEffect.ReleaseFrozenSnapshot();
            }
        }
        if (curtainOn && hole != _shownBurn)
        {
            _shownBurn = hole;
            _uBurn[0] = hole;
            _curtainEffect.SetUniform("uBurn", _uBurn);
        }
        // The countdown: each number jumps in and settles; GO stays a moment into the run.
        _go = Math.Max(_go - delta, 0.0f);
        int show = count is uint n ? (int)n
            : phase == Phase.Over ? (escaped ? -3 : -2)
            : _go > 0.0f ? 0
            : -1;
        if (show != _shownCount)
        {
            _shownCount = show;
            _countPop = 1.0f;
            if (show != -1)
            {
                _countLabel.FontSize = show >= 0 ? 120.0 : _canvasWidth < 520.0f ? 38.0 : 64.0;
                _countLabel.Text = show switch
                {
                    0 => "GO",
                    -2 => "GAME OVER",
                    -3 => "YOU ESCAPED!",
                    _ => show.ToString(),
                };
                // Fire, or the title's gold for the way out.
                _countLabel.FillGradient = Fire(show == -3 ? GOLD : FIRE);
            }
            _count.IsVisible = show != -1;
            _count.Opacity = 1.0;
            // GAME OVER and the caption under it are centered as one block.
            _count.TranslationY = show == -2 ? -GAME_OVER_LIFT : 0.0;
            if (show == -3)
                Tint(ACCENT).CopyTo(_uCountTint, 0);
            else
                (_uCountTint[0], _uCountTint[1], _uCountTint[2]) = (1.0f, 0.16f, 0.04f);
            _countGlow.SetUniform("uTint", _uCountTint);
            if (show == -3)
            {
                // The time stays with the view from outside, until the player leaves it.
                (_notice, _hint, _pending) = (float.MaxValue, Hint.None, null);
                ShowPrompt(new Prompt(PromptKind.Time, Tenths: (uint)(runTime * 10.0f)));
            }
        }
        if (_countPop > 0.0f)
        {
            _countPop = Math.Max(_countPop - delta * 2.4f, 0.0f);
            double scale = 1.0 + 0.7 * _countPop * _countPop;
            _count.ScaleX = scale;
            _count.ScaleY = scale;
        }
        // A hit (not the drain) ends the clean stretch and, the first time, teaches the orbs.
        if (hits != _shownHits)
        {
            _shownHits = hits;
            if (hits > 0)
            {
                _clean = 0.0f;
                if (!_taughtHeal && phase == Phase.Playing)
                    (_taughtHeal, _pending) = (true, new Prompt(PromptKind.Heal));
            }
        }
        // The bar changes by whole cells (the drain moves the health every frame). The second cell lost in a session
        // explains the drain.
        int lit = Math.Max((int)MathF.Round(health * 10.0f, MidpointRounding.AwayFromZero), 0);
        if (lit != _shownCells)
        {
            if (lit < _shownCells && lit <= 8 && !_taughtDrain && phase == Phase.Playing)
                (_taughtDrain, _pending) = (true, new Prompt(PromptKind.Drain));
            _shownCells = lit;
            // Under four cells: LOW HEALTH, once per fall under the line.
            bool low = lit < 4;
            if (low && !_warnedLow && phase == Phase.Playing && health > 0.0f)
                _pending = new Prompt(PromptKind.Low);
            _warnedLow = low;
            // Green, amber, red; a cell per tenth, lit from the left.
            var color = C(health > 0.6f ? GOOD : health > 0.3f ? 0xFFFF_B030 : 0xFFFF_3B30);
            _hud.AccessibilityLabel = $"Health {lit * 10} percent";
            for (int i = 0; i < _cells.Length; i++)
            {
                bool on = i < lit;
                _cells[i].BackgroundColor = on ? color : C(CELL_OFF);
                _cells[i].Shadows = on
                    ? new List<SkiaShadow> { new() { Color = color, X = 0, Y = 0, Blur = 7, Opacity = 0.9 } }
                    : new List<SkiaShadow>();
            }
        }
        // An orb taken: the distance jumps in a violet flash.
        if (phase == Phase.Playing && orbs != _shownScore)
        {
            bool won = orbs > _shownScore;
            _shownScore = orbs;
            if (won)
            {
                FlashScore();
                // The second and third orb of a row say so.
                if (streak >= 2)
                {
                    _streakTime = 0.9f;
                    _streak.Text = $"x{streak}";
                    _streak.Opacity = 1.0;
                }
            }
        }
        if (pickup != null && phase == Phase.Playing)
            FlashScore();
        if (phase == Phase.Playing && meters != _shownDistance)
        {
            _shownDistance = meters;
            _score.Text = $"{meters} M";
            _score.AccessibilityLabel = $"Distance {meters} meters";
        }
        if (_streakTime > 0.0f)
        {
            _streakTime = Math.Max(_streakTime - delta, 0.0f);
            _streak.Opacity = Math.Min(_streakTime / 0.3f, 1.0f);
        }
        if (_pop > 0.0f)
        {
            _pop = Math.Max(_pop - delta * 3.0f, 0.0f);
            double scale = 1.0 + 0.35 * _pop * _pop;
            _score.ScaleX = scale;
            _score.ScaleY = scale;
            // The flash is over: no shader left over the score.
            if (_pop <= 0.0f)
            {
                _scoreBox.VisualEffects.Clear();
            }
            else
            {
                _uScoreGlow[0] = _pop;
                _scoreGlow.SetUniform("uGlow", _uScoreGlow);
            }
        }
        if (phase == Phase.Playing)
            _clean += delta;
        // The first ghost gone by teaches (after a touch, once the blackout is over).
        if (ghostsPassed > 0 && !_taughtGhost && phase == Phase.Playing)
            (_taughtGhost, _pending) = (true, new Prompt(PromptKind.Avoid));
        // The ghost's kill takes the prompt away; a power-up says its name for a moment; the lessons wait.
        if ((worldKiller != null) != _ghosted)
        {
            _ghosted = worldKiller != null;
            if (_ghosted)
            {
                (_notice, _hint, _pending) = (0.0f, Hint.None, null);
                ShowPrompt(null);
            }
        }
        if (_pending is Prompt pending && phase == Phase.Playing && _notice <= 0.0f && hint == Hint.None && worldGhost < 0.5f)
        {
            (_pending, _notice, _hint) = (null, 1.8f, Hint.None);
            ShowPrompt(pending);
        }
        else if (pickup is byte power && phase == Phase.Playing)
        {
            (_notice, _hint) = (1.3f, Hint.None);
            ShowPrompt(new Prompt(power == World.HEALTH ? PromptKind.Health : power == World.SHIELD ? PromptKind.Shield : PromptKind.Surge));
        }
        else if (_notice > 0.0f)
        {
            _notice -= delta;
            if (_notice <= 0.0f)
                ShowPrompt(null);
        }
        else if (phase == Phase.Playing && _clean >= CHEER_EVERY && hint == Hint.None)
        {
            // A stretch without a hit: a word of praise, in the prompt's slot while no lesson needs it.
            _clean = 0.0f;
            _cheers += 1;
            _notice = 1.3f;
            ShowPrompt(new Prompt(PromptKind.Cheer, CHEERS[(int)(unchecked(_cheers * 7 + meters) % (uint)CHEERS.Length)]));
        }
        else if (hint != _hint)
        {
            _hint = hint;
            int lesson = hint switch { Hint.Jump => 0, Hint.Lane => 1, _ => -1 };
            // Every hazard of a young run is announced; later the first three of each kind teach it.
            bool showHint = false;
            if (lesson >= 0)
            {
                if (_taught[lesson] < 4)
                    _taught[lesson] += 1;
                showHint = young || _taught[lesson] <= 3;
            }
            ShowPrompt(showHint ? new Prompt(hint == Hint.Jump ? PromptKind.Jump : PromptKind.Lane) : null);
        }
        // Under GAME OVER, once it takes a key: how to go on (any key works, Space is the one named).
        bool restart = phase == Phase.Over && !escaped && _overReady && _leaving == 0;
        if (restart != _restartShown)
        {
            _restartShown = restart;
            _restart.Text = _touched ? "TAP TO CONTINUE" : "TAP OR PRESS SPACE TO CONTINUE";
            _restart.Opacity = restart ? 1.0 : 0.0;
        }
        // The prompt jumps in like the countdown, then its glow beats.
        if (_promptPop > 0.0f || _promptTime > 0.0f)
        {
            _promptPop = Math.Max(_promptPop - delta * 2.4f, 0.0f);
            _promptTime += delta;
            double scale = 1.0 + 0.7 * _promptPop * _promptPop;
            _promptBox.ScaleX = scale;
            _promptBox.ScaleY = scale;
            _uPromptGlow[0] = 0.55f + 0.45f * _promptPop + 0.3f * MathF.Sin(_promptTime * 9.0f);
            _promptGlow.SetUniform("uGlow", _uPromptGlow);
        }
    }

    /// <summary>Tenths of a second as m:ss.t.</summary>
    static string FormatTime(uint tenths) => $"{tenths / 600}:{tenths / 10 % 60:00}.{tenths % 10}";

    /// <summary>Shows the prompt (text, gradient and glow of its kind, jumping in) or hides it.</summary>
    void ShowPrompt(Prompt? prompt)
    {
        if (prompt is Prompt p)
        {
            (string text, uint[] colors, float[] tint) = p.Kind switch
            {
                PromptKind.Time => ($"YOUR TIME {FormatTime(p.Tenths)}", GOLD, Tint(ACCENT)),
                PromptKind.Jump => ("JUMP", new uint[] { 0xFFF4_E8FF, 0xFFB0_70FF, 0xFF70_20E0 }, new[] { 0.62f, 0.22f, 1.0f }),
                PromptKind.Lane => ("CHANGE LANE", new uint[] { 0xFFE8_FFFF, 0xFF40_E0FF, 0xFF10_90E0 }, new[] { 0.15f, 0.85f, 1.0f }),
                // Health shows during its own green flash: white letters with a green glow stay readable in it.
                PromptKind.Health => ("HEALTH UP", new uint[] { 0xFFFF_FFFF, 0xFFF0_FFE8, 0xFFA0_E890 }, new[] { 0.42f, 1.0f, 0.35f }),
                PromptKind.Surge => ("SURGE", new uint[] { 0xFFFF_FFFF, 0xFF90_F0FF, 0xFF30_B0FF }, new[] { 0.5f, 0.9f, 1.0f }),
                PromptKind.Shield => ("SHIELD ON", GOLD, new[] { 1.0f, 0.8f, 0.3f }),
                PromptKind.Absorbed => ("ABSORBED!", GOLD, new[] { 1.0f, 0.8f, 0.3f }),
                PromptKind.Cheer => (p.Cheer, GOLD, Tint(ACCENT)),
                PromptKind.Heal => ("COLLECT ORBS TO HEAL", new uint[] { 0xFFFF_FFFF, 0xFFF0_FFE8, 0xFFA0_E890 }, new[] { 0.42f, 1.0f, 0.35f }),
                PromptKind.Drain => ("YOU TIRE AS YOU RUN", new uint[] { 0xFFFF_FFFF, 0xFFF0_FFE8, 0xFFA0_E890 }, new[] { 0.42f, 1.0f, 0.35f }),
                PromptKind.Avoid => ("AVOID GHOSTS", new uint[] { 0xFFFF_FFFF, 0xFFD8_E4FF, 0xFF88_A0E0 }, new[] { 0.72f, 0.84f, 1.0f }),
                _ => ("LOW HEALTH", new uint[] { 0xFFFF_E0D8, 0xFFFF_6050, 0xFFC0_1810 }, new[] { 1.0f, 0.25f, 0.2f }),
            };
            _prompt.Text = text;
            _prompt.FillGradient = Fire(colors);
            (_promptPop, _promptTime) = (1.0f, 0.001f);
            _promptBox.IsVisible = true;
            tint.CopyTo(_uPromptTint, 0);
            _promptGlow.SetUniform("uTint", _uPromptTint);
        }
        else
        {
            (_promptPop, _promptTime) = (0.0f, 0.0f);
            _promptBox.IsVisible = false;
        }
    }
}
