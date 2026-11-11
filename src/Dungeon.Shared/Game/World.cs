// The rules of the run (port of examples/dungeon/src/scene.rs, the `World` half). Pure C#, no DrawnUI:
// the test project compiles this file alone. The corridor is straight in the world; the camera bends it
// with depth (Scene.cs), so the rules stay simple and far to near is always the drawing order.

namespace DungeonRun;

/// <summary>What stands in a segment.</summary>
public enum RowKind : byte
{
    Empty,
    /// <summary>An orb in this lane (-1, 0, 1).</summary>
    Orb,
    /// <summary>Pillars in the lanes of the mask (bit = lane + 1): change lane.</summary>
    Pillars,
    /// <summary>A beam across the corridor: jump.</summary>
    Beam,
    /// <summary>A lava pit: jump.</summary>
    Lava,
    /// <summary>A power-up in this lane: HEALTH, SURGE, GHOST or SHIELD.</summary>
    Power,
}

/// <summary>A row of the run: its kind and the lane, mask or power that goes with it.</summary>
public readonly struct Row : IEquatable<Row>
{
    public readonly RowKind Kind;
    public readonly int Lane;
    public readonly byte Mask;
    public readonly byte Power;

    Row(RowKind kind, int lane = 0, byte mask = 0, byte power = 0)
    {
        Kind = kind;
        Lane = lane;
        Mask = mask;
        Power = power;
    }

    public static readonly Row Empty = new(RowKind.Empty);
    public static readonly Row Beam = new(RowKind.Beam);
    public static readonly Row Lava = new(RowKind.Lava);
    public static Row Orb(int lane) => new(RowKind.Orb, lane);
    public static Row Pillars(byte mask) => new(RowKind.Pillars, mask: mask);
    public static Row PowerUp(int lane, byte power) => new(RowKind.Power, lane, power: power);

    public bool IsJump => Kind is RowKind.Beam or RowKind.Lava;
    public bool IsGhost => Kind == RowKind.Power && Power == World.GHOST;

    public bool Equals(Row other) => Kind == other.Kind && Lane == other.Lane && Mask == other.Mask && Power == other.Power;
    public override bool Equals(object obj) => obj is Row other && Equals(other);
    public override int GetHashCode() => (int)Kind | Lane << 8 | Mask << 16 | Power << 24;
    public static bool operator ==(Row a, Row b) => a.Equals(b);
    public static bool operator !=(Row a, Row b) => !a.Equals(b);
    public override string ToString() => Kind switch
    {
        RowKind.Orb => $"Orb({Lane})",
        RowKind.Pillars => $"Pillars({Mask})",
        RowKind.Power => $"Power({Lane}, {Power})",
        _ => Kind.ToString(),
    };
}

public enum Phase
{
    /// <summary>The game plays itself behind the title.</summary>
    Attract,
    /// <summary>Three, two, one: the runner stands, nothing is taken from the player yet.</summary>
    Countdown,
    Playing,
    /// <summary>The health is gone: the runner goes down.</summary>
    Dead,
    /// <summary>Down: GAME OVER stays until a key or a tap brings the title back.</summary>
    Over,
}

/// <summary>What a new player should do about what comes next.</summary>
public enum Hint
{
    None,
    Jump,
    Lane,
}

/// <summary>What the player asked for since the last step.</summary>
public struct Input
{
    public bool Left;
    public bool Right;
    public bool Jump;
}

public sealed class World
{
    /// <summary>Length of a corridor segment: one row of the run.</summary>
    public const float SEG = 2.0f;
    public const float HALF_WIDTH = 3.0f;
    public const float HEIGHT = 5.0f;
    /// <summary>Distance between the three lanes.</summary>
    public const float LANE = 2.0f;
    /// <summary>The dungeon ends here: a doorway of light. The last hazards stand 100 units before it.</summary>
    public const double EXIT = 3000.0;
    /// <summary>From this far before the door the way out is in sight (the corridor is straight by then).</summary>
    public const double EXIT_SIGHT = 300.0;
    public const long EXIT_ROW = (long)(EXIT / 2.0);
    /// <summary>How long GAME OVER's frozen picture takes to burn away over the title.</summary>
    const float BURN_SECONDS = 1.4f;
    /// <summary>A zone of one palette.</summary>
    const double ZONE = 400.0;

    /// <summary>Torch, accent (lava, runes) and fog colors per zone.</summary>
    static readonly float[][][] PALETTES =
    [
        [[1.0f, 0.55f, 0.22f], [1.0f, 0.33f, 0.04f], [0.045f, 0.02f, 0.03f]],
        [[0.35f, 0.65f, 1.0f], [0.15f, 0.85f, 1.0f], [0.012f, 0.03f, 0.07f]],
        [[0.55f, 1.0f, 0.3f], [0.7f, 1.0f, 0.08f], [0.02f, 0.05f, 0.022f]],
    ];

    /// <summary>Gives 15 percent of the health back.</summary>
    public const byte HEALTH = 0;
    /// <summary>What one orb gives back: ten orbs fill one cell of the health (the green cross fills two).</summary>
    const float ORB_HEALTH = 0.01f;
    /// <summary>Four seconds much faster, through everything unhurt.</summary>
    public const byte SURGE = 1;
    /// <summary>The dungeon ghost: it hangs in a lane like a power-up and takes three tenths of the health.</summary>
    public const byte GHOST = 2;
    /// <summary>A golden shield: the next pillar or ghost costs nothing (beams and lava still burn).</summary>
    public const byte SHIELD = 3;

    /// <summary>The first orbs and the first hazards of a run.</summary>
    const long ORBS_FROM = 6;
    const long HAZARDS_FROM = 16;
    /// <summary>
    /// The run gets its full share of hazards from this row on; before it the share climbs from a half, and the
    /// first beams come at JUMPS_FROM (about 13 seconds in). Lava comes in one event row of four from the first hazards on.
    /// </summary>
    public const long RAMP_UNTIL = 304;
    const long JUMPS_FROM = 90;
    /// <summary>Seconds before a hazard its prompt shows (longer while the run is young).</summary>
    const float HINT_LEAD = 1.2f;
    /// <summary>The run's speed: from the start to the top, gaining 0.012 per unit of distance.</summary>
    const float START_SPEED = 10.0f;
    const float TOP_SPEED = 24.0f;
    /// <summary>The first orb flash, violet: every zone's flash has as much light.</summary>
    static readonly float[] FIRST_FLASH = [0.62f * 0.32f, 0.3f * 0.32f, 0.32f];
    /// <summary>Seconds of running that cost one cell of health.</summary>
    const float DRAIN_SECONDS = 4.0f;
    /// <summary>How long a shield holds.</summary>
    const float SHIELD_SECONDS = 10.0f;

    // ---- the run as a function of the row number ----

    public static uint Hash(long i, ulong salt)
    {
        unchecked
        {
            ulong x = (ulong)i * 0x9E3779B97F4A7C15UL ^ salt * 0xBF58476D1CE4E5B9UL;
            x ^= x >> 30;
            x *= 0xBF58476D1CE4E5B9UL;
            x ^= x >> 27;
            x *= 0x94D049BB133111EBUL;
            return (uint)(x >> 33);
        }
    }

    public static float Hash01(long i, ulong salt) => (Hash(i, salt) & 0xFFFF) / 65536.0f;

    /// <summary>Rust i64::div_euclid (floor division for a positive divisor).</summary>
    public static long DivEuclid(long a, long b)
    {
        var q = a / b;
        if (a % b < 0)
            q = b > 0 ? q - 1 : q + 1;
        return q;
    }

    /// <summary>Rust i64::rem_euclid (never negative).</summary>
    public static long RemEuclid(long a, long b)
    {
        var r = a % b;
        return r < 0 ? r + Math.Abs(b) : r;
    }

    static byte LaneBit(int lane) => (byte)(1 << (lane + 1));

    static int CountOnes(byte mask) => (mask & 1) + (mask >> 1 & 1) + (mask >> 2 & 1);

    /// <summary>The lane the orbs of a group of five rows hang in; the pillars after them leave it free.</summary>
    public static int OrbLane(long group) => (int)(Hash(group, 1) % 3) - 1;

    /// <summary>The one lane a double pillar row leaves free, or null.</summary>
    static int? OnlyFree(long i)
    {
        var r = GetRow(i);
        if (r.Kind != RowKind.Pillars || CountOnes(r.Mask) != 2)
            return null;
        for (int lane = -1; lane <= 1; lane++)
            if ((r.Mask & LaneBit(lane)) == 0)
                return lane;
        return null;
    }

    /// <summary>
    /// The ghost's lane: any, but never the one lane a double pillar row leaves free, neither the row just before
    /// it (no time to step aside) nor the row just after it (no lane to step into).
    /// </summary>
    static int GhostLane(long group)
    {
        var before = OnlyFree(group * 5);
        var after = OnlyFree((group + 1) * 5);
        int lane = (int)(Hash(group, 10) % 3) - 1;
        while (lane == before || lane == after)
            lane = lane == 1 ? -1 : lane + 1;
        return lane;
    }

    // Row is a pure function of its number, asked for hundreds of times a frame (the corridor, its lava glow,
    // the hints, the dread, the pilot): remembered per thread in a small direct-mapped table.
    const int RowCacheSize = 1024;
    [ThreadStatic] static long[] t_rowKeys;
    [ThreadStatic] static Row[] t_rowValues;

    /// <summary>The run is a function of the row number: an event every fifth row, orbs in between.</summary>
    public static Row GetRow(long i)
    {
        var keys = t_rowKeys;
        if (keys == null)
        {
            keys = t_rowKeys = new long[RowCacheSize];
            Array.Fill(keys, long.MinValue);
            t_rowValues = new Row[RowCacheSize];
        }
        int slot = (int)(i & (RowCacheSize - 1));
        if (keys[slot] == i)
            return t_rowValues[slot];
        var row = ComputeRow(i);
        // the recursion inside ComputeRow may have taken another slot only: this one is still ours
        t_rowKeys[slot] = i;
        t_rowValues[slot] = row;
        return row;
    }

    static Row ComputeRow(long i)
    {
        // Orbs from the first second, the first hazards a couple of seconds later.
        if (i < ORBS_FROM || i >= EXIT_ROW - 50)
            return Row.Empty;
        long group = DivEuclid(i, 5);
        switch (RemEuclid(i, 5))
        {
            case 0:
            {
                // A gentle start: half the hazards, lane changes before jumps, the full share after a while.
                if (i < HAZARDS_FROM)
                    return Row.Empty;
                float ramp = Math.Clamp((float)(i - HAZARDS_FROM) / (float)(RAMP_UNTIL - HAZARDS_FROM), 0.0f, 1.0f);
                // Of a hundred event rows: 38 pillars, 20 beams, 25 lava, the rest empty. Lava skips the ramp.
                uint h = Hash(group, 2);
                uint roll = h % 100;
                bool lava = roll >= 75;
                if (!lava && Hash01(group, 7) > 0.5f + 0.5f * ramp)
                    return Row.Empty;
                // While the run is slow, a jump lasts about one event row: a jump right after a jump leaves no
                // time to land, so the second is pillars.
                bool landing = i < RAMP_UNTIL && GetRow(i - 5).IsJump;
                if (roll < 38 || (roll < 58 && i < JUMPS_FROM) || ((roll < 58 || lava) && landing))
                {
                    int o0, o1;
                    switch (OrbLane(group - 1))
                    {
                        case -1: (o0, o1) = (0, 1); break;
                        case 0: (o0, o1) = (-1, 1); break;
                        default: (o0, o1) = (-1, 0); break;
                    }
                    return Row.Pillars(((h >> 8) % 3) switch
                    {
                        0 => LaneBit(o0),
                        1 => LaneBit(o1),
                        _ => (byte)(LaneBit(o0) | LaneBit(o1)),
                    });
                }
                if (roll >= 38 && roll <= 57)
                    return Row.Beam;
                if (roll >= 75)
                    return Row.Lava;
                return Row.Empty;
            }
            // Now and then the middle orb of a group is a power-up: health in one group of twelve, a surge in one
            // of sixteen, a shield in one of twelve, the ghost in 18 % of the groups after the first thirty rows.
            case 3:
            {
                uint pick = Hash(group, 6) % 48;
                if (i > 30 && Hash(group, 9) % 100 < 18)
                    return Row.PowerUp(GhostLane(group), GHOST);
                if (pick <= 3)
                    return Row.PowerUp(OrbLane(group), HEALTH);
                if (pick <= 6)
                    return Row.PowerUp(OrbLane(group), SURGE);
                if (pick <= 10)
                    return Row.PowerUp(OrbLane(group), SHIELD);
                return Row.Orb(OrbLane(group));
            }
            case 2:
            case 4:
                return Row.Orb(OrbLane(group));
            default:
                return Row.Empty;
        }
    }

    /// <summary>How much lava lights the boundary before segment <paramref name="j"/>.</summary>
    public static float LavaGlow(long j)
    {
        static bool IsLava(long i) => GetRow(i).Kind == RowKind.Lava;
        if (IsLava(j) || IsLava(j - 1))
            return 1.0f;
        if (IsLava(j + 1) || IsLava(j - 2))
            return 0.35f;
        return 0.0f;
    }

    // ---- state ----

    public Phase Phase = Phase.Attract;
    /// <summary>Distance run.</summary>
    internal double Z;
    internal float X;
    internal float Y;
    float _vy;
    int _lane;
    internal float Speed = 12.0f;
    public float Time;
    uint _orbs;
    /// <summary>1 = unhurt; a pillar takes two tenths, a beam one, lava a half; the run ends at 0.</summary>
    public float Health = 1.0f;
    /// <summary>Seconds left in which nothing hurts, after a hit.</summary>
    float _invulnerable;
    /// <summary>The distance of the last run and the best one.</summary>
    public uint Last;
    public uint LastDistance;
    public uint Best;
    /// <summary>Rows whose orb was taken, the newest 64.</summary>
    readonly List<long> _collected = new(64);
    /// <summary>Orbs taken in a row (their rows follow each other), and the row of the last one.</summary>
    public uint Streak;
    long _lastOrb = -10;
    /// <summary>The border's violet pulse of an orb, fading.</summary>
    public float OrbPulse;
    float _deadTimer;
    /// <summary>1 at a hit, fading.</summary>
    public float Flash;
    /// <summary>1 at a portal (a new zone, a start), fading.</summary>
    public float Warp;
    internal float Shake;
    long _zone;
    /// <summary>Sideways speed, smoothed: the camera leans into a lane change.</summary>
    internal float Lean;
    float _pilotWait;
    /// <summary>Seconds of countdown left.</summary>
    float _count;
    /// <summary>0 to 1: how far the runner went down after the last hit.</summary>
    internal float Fall;
    /// <summary>1 when health was just picked up, fading: the green flash and its sparks.</summary>
    public float Heal;
    /// <summary>1 when the ghost just took the run, fading: the pale flash.</summary>
    public float Ghost;
    /// <summary>The row of the ghost that took the run: it stays, over the fallen runner.</summary>
    public long? Killer;
    /// <summary>0..1: a ghost is near ahead: the dungeon darkens.</summary>
    public float Dread;
    /// <summary>The ghost that lunged last.</summary>
    long? _revealed;
    /// <summary>Ghosts the runner has gone past (hit or not) in this run.</summary>
    public uint GhostsPassed;
    /// <summary>Hits taken in this run (the drain is not a hit).</summary>
    public uint Hits;
    /// <summary>Seconds the shield still holds (ten from its pickup).</summary>
    public float Shield;
    /// <summary>1 when the shield just took a hit, fading (the golden flash).</summary>
    public float ShieldBurst;
    /// <summary>The runner reached the exit: the run ends in light, not on the floor.</summary>
    public bool Escaped;
    /// <summary>How far the world outside the door is lifted toward white (eyes used to the dark).</summary>
    public float Glare = 0.3f;
    /// <summary>Seconds of the run.</summary>
    public float RunTime;
    /// <summary>The fastest escape of the session.</summary>
    public float? BestTime;
    /// <summary>0 to 1, the white-out after the escape.</summary>
    public float Escape;
    /// <summary>0 to 1, eased: how strong the rays of a surge are.</summary>
    public float SurgeGlow;
    /// <summary>1 to 0 after GAME OVER: how much of its frozen picture still covers the title's run.</summary>
    public float Burn;
    /// <summary>1 = full speed; a pillar stops the runner (0), a beam or lava slows it, then it picks up.</summary>
    float _pace = 1.0f;
    /// <summary>Seconds of surge left.</summary>
    float _surge;
    /// <summary>The power-up taken in the last step, until the app asks.</summary>
    byte? _pickup;

    /// <summary>How many orbs were taken (the third of a row counts double).</summary>
    public uint Orbs => _orbs;

    /// <summary>How far the run got, in units of the dungeon (shown as meters).</summary>
    public uint Distance => (uint)Z;

    /// <summary>Whether the orb of row <paramref name="i"/> was taken.</summary>
    public bool Collected(long i) => _collected.Contains(i);

    /// <summary>The run is still in its calm start: every hazard comes with its prompt.</summary>
    public bool Young => Z < RAMP_UNTIL * 2.0;

    /// <summary>0..1: how fast the run is, for the effects.</summary>
    public float Rush => _surge > 0.0f ? 1.0f : Math.Clamp((Speed - START_SPEED) / (TOP_SPEED - START_SPEED), 0.0f, 1.0f);

    /// <summary>The move the nearest hazard ahead asks for, from HINT_LEAD seconds before it (more while young).</summary>
    public Hint GetHint()
    {
        if (Phase != Phase.Playing)
            return Hint.None;
        float lead = Speed * (Young ? HINT_LEAD * 1.3f : HINT_LEAD);
        long first = (long)Math.Floor(Z / 2.0);
        for (long i = first; i < first + 40; i++)
        {
            float distance = (float)(i * 2.0 + 1.0 - Z);
            if (distance > lead)
                return Hint.None;
            var row = GetRow(i);
            var hint = Hint.None;
            if (row.IsJump && Y <= 0.0f)
                hint = Hint.Jump;
            else if (row.Kind == RowKind.Pillars && (row.Mask & LaneBit(_lane)) != 0)
                hint = Hint.Lane;
            if (hint != Hint.None && distance >= 0.0f)
                return hint;
        }
        return Hint.None;
    }

    /// <summary>An orb is taken: a little health back, the borders pulse; the third orb of a row counts double.</summary>
    void TakeOrb(long row)
    {
        Streak = row == _lastOrb + 1 ? Streak + 1 : 1;
        _lastOrb = row;
        uint count = Streak == 3 ? 2u : 1u;
        _orbs += count;
        Health = Math.Min(Health + ORB_HEALTH * count, 1.0f);
        float power = 1.0f + 0.3f * (Math.Min(Streak, 3) - 1);
        OrbPulse = 0.55f * power;
    }

    /// <summary>The power-up taken since the last call.</summary>
    public byte? TakePickup()
    {
        var pickup = _pickup;
        _pickup = null;
        return pickup;
    }

    void Restart(Phase phase)
    {
        (Z, X, Y, _vy, _lane, _orbs, _zone) = (0.0, 0.0f, 0.0f, 0.0f, 0, 0u, 0L);
        (Health, _invulnerable, _surge, _pace, Fall) = (1.0f, 0.0f, 0.0f, 1.0f, 0.0f);
        _collected.Clear();
        (Ghost, Killer, Dread, _revealed, GhostsPassed, Hits) = (0.0f, null, 0.0f, null, 0u, 0u);
        (Escaped, RunTime, Escape, Glare) = (false, 0.0f, 0.0f, 0.3f);
        (Shield, ShieldBurst) = (0.0f, 0.0f);
        (Streak, _lastOrb, OrbPulse) = (0u, -10L, 0.0f);
        Phase = phase;
    }

    /// <summary>The health is gone: the runner goes down.</summary>
    void Die()
    {
        Health = 0.0f;
        Phase = Phase.Dead;
        // Under the ghost the runner lies a while longer.
        _deadTimer = Killer != null ? 2.6f : 1.6f;
        (Last, LastDistance) = (Distance, Distance);
        Best = Math.Max(Best, Last);
    }

    /// <summary>A new run, through a portal.</summary>
    public void Start()
    {
        Restart(Phase.Countdown);
        (_count, Warp) = (3.0f, 0.35f);
    }

    /// <summary>Back to the title and its own run.</summary>
    public void ToTitle()
    {
        Restart(Phase.Attract);
        Burn = 1.0f;
    }

    /// <summary>Seconds GAME OVER (or the view from outside) has been standing.</summary>
    public float OverFor => Phase == Phase.Over ? -_deadTimer : 0.0f;

    /// <summary>The number the countdown shows: 3, 2, 1.</summary>
    public uint? Count => Phase == Phase.Countdown ? (uint)Math.Max(MathF.Ceiling(_count), 1.0f) : null;

    public void Step(float dt, ref Input input)
    {
        Time += dt;
        Flash = Math.Max(Flash - dt * 1.6f, 0.0f);
        Warp = Math.Max(Warp - dt * 1.1f, 0.0f);
        Shake = Math.Max(Shake - dt * 2.5f, 0.0f);
        _invulnerable = Math.Max(_invulnerable - dt, 0.0f);
        Heal = Math.Max(Heal - dt * 1.1f, 0.0f);
        Ghost = Math.Max(Ghost - dt * 0.7f, 0.0f);
        ShieldBurst = Math.Max(ShieldBurst - dt * 1.4f, 0.0f);
        float glare = Z >= EXIT ? 0.0f : 0.15f + 0.15f * (float)Math.Clamp((EXIT - Z) / EXIT_SIGHT, 0.0, 1.0);
        Glare += (glare - Glare) * (1.0f - MathF.Exp(-dt * 2.0f));
        if (Phase == Phase.Playing)
            Shield = Math.Max(Shield - dt, 0.0f);

        // The nearest ghost ahead: dread grows over the last three seconds to it, and when it is less than a
        // second away it reveals itself with a jolt.
        float dread = 0.0f;
        if (Phase == Phase.Playing)
        {
            long first = (long)Math.Floor(Z / 2.0);
            for (long i = first; i < first + 60; i++)
            {
                if (GetRow(i).IsGhost)
                {
                    float distance = (float)(i * 2.0 + 1.0 - Z);
                    if (distance > 0.0f)
                    {
                        dread = Math.Clamp(1.0f - distance / (Speed * 3.0f), 0.0f, 1.0f);
                        if (distance < Speed * 0.9f && _revealed != i)
                        {
                            _revealed = i;
                            (Shake, Warp) = (Math.Max(Shake, 0.6f), Math.Max(Warp, 0.7f));
                        }
                    }
                    break;
                }
            }
        }
        else if (Killer != null)
        {
            dread = 1.0f;
        }
        Dread += (dread - Dread) * (1.0f - MathF.Exp(-dt * 6.0f));
        if (_revealed is long revealed && Z > revealed * 2.0 + 1.5)
        {
            GhostsPassed += 1;
            _revealed = null;
        }
        OrbPulse = Math.Max(OrbPulse - dt * 4.5f, 0.0f);

        // Full for the first second of a surge, then fading with the time left.
        float surging = Phase != Phase.Dead ? Math.Clamp(_surge / 3.0f, 0.0f, 1.0f) : 0.0f;
        SurgeGlow += (surging - SurgeGlow) * (1.0f - MathF.Exp(-dt * 5.0f));
        if (SurgeGlow < 0.004f)
            SurgeGlow = 0.0f;
        if (Phase != Phase.Over)
            Burn = Math.Max(Burn - dt / BURN_SECONDS, 0.0f);
        // Down, the picture stays red.
        Flash = Math.Max(Flash, 0.3f * Fall);

        var taken = input;
        input = default;
        switch (Phase)
        {
            case Phase.Dead:
                taken = default;
                Speed *= MathF.Exp(-dt * 4.0f);
                _deadTimer -= dt;
                if (Escaped)
                {
                    // Drifts on into the light, slowing.
                    Speed *= MathF.Exp(-dt * 1.2f);
                    Escape = Math.Min(Escape + dt / 1.4f, 1.0f);
                }
                else
                {
                    Fall = Math.Min(Fall + dt / 1.1f, 1.0f);
                }
                if (_deadTimer <= 0.0f)
                    Phase = Phase.Over;
                break;
            case Phase.Over:
                taken = default;
                Speed = Escaped ? Speed * MathF.Exp(-dt * 1.2f) : 0.0f;
                _deadTimer -= dt;
                if (Escaped)
                    Escape = Math.Min(Escape + dt / 1.4f, 1.0f);
                // GAME OVER and the view from outside stay until a key or a tap (ToTitle).
                break;
            case Phase.Countdown:
            {
                (taken, Speed) = (default, 0.0f);
                float before = MathF.Ceiling(_count);
                _count -= dt;
                if (_count <= 0.0f)
                    (Phase, Warp) = (Phase.Playing, 1.0f); // off, through a portal
                else if (MathF.Ceiling(_count) != before)
                    Warp = Math.Max(Warp, 0.35f);
                break;
            }
            case Phase.Attract:
                taken = Pilot(dt);
                Speed = 14.0f;
                // The title's run never shows the end: it starts over before the exit's light.
                if (Z >= EXIT - 700.0)
                    Restart(Phase.Attract);
                break;
            case Phase.Playing:
                Speed = Math.Min(START_SPEED + (float)Z * 0.012f, TOP_SPEED);
                RunTime += dt;
                // The run wears the runner down: a cell of health every DRAIN_SECONDS.
                Health -= dt / (DRAIN_SECONDS * 10.0f);
                if (Health < 0.005f)
                    Die();
                if (Z >= EXIT + 1.0)
                {
                    // Out, into the light: the run ends like a death without the fall.
                    (Escaped, Phase, _deadTimer, Warp) = (true, Phase.Dead, 1.8f, 1.0f);
                    (Last, LastDistance) = (Distance, Distance);
                    BestTime = BestTime is float best ? Math.Min(best, RunTime) : RunTime;
                }
                break;
        }
        if (_surge > 0.0f && Phase != Phase.Dead)
        {
            _surge -= dt;
            Speed *= 1.5f;
        }

        if (taken.Left)
            _lane = Math.Max(_lane - 1, -1);
        if (taken.Right)
            _lane = Math.Min(_lane + 1, 1);
        if (taken.Jump && Y <= 0.0f)
            _vy = 7.6f;
        float beforeX = X;
        X += (_lane * LANE - X) * (1.0f - MathF.Exp(-dt * 13.0f));
        float sideways = (X - beforeX) / Math.Max(dt, 1e-4f);
        Lean += (sideways * 0.018f - Lean) * (1.0f - MathF.Exp(-dt * 10.0f));
        Y += _vy * dt;
        _vy -= 22.0f * dt;
        if (Y <= 0.0f)
            (Y, _vy) = (0.0f, Math.Max(_vy, 0.0f));

        // The runner is a point half a unit ahead of the camera; what it passed through this step.
        _pace = Math.Min(_pace + dt / 1.1f, 1.0f);
        double p0 = Z + 0.5, p1 = Z + (double)(Speed * _pace * _pace * dt) + 0.5;
        Z = p1 - 0.5;
        if (Phase is Phase.Playing or Phase.Attract)
        {
            long from = (long)Math.Floor(p0 / 2.0), to = (long)Math.Floor(p1 / 2.0);
            for (long i = from; i <= to; i++)
            {
                double z0 = i * 2.0;
                bool Touches(double a, double b) => p1 >= z0 + a && p0 <= z0 + b;
                bool Near(int lane, float reach) => Math.Abs(X - lane * LANE) < reach;
                var kind = GetRow(i);
                float damage = 0.0f;
                switch (kind.Kind)
                {
                    case RowKind.Pillars:
                        if (Touches(0.4, 1.6))
                            for (int lane = -1; lane <= 1; lane++)
                                if ((kind.Mask & LaneBit(lane)) != 0 && Near(lane, 1.0f))
                                {
                                    damage = 0.2f;
                                    break;
                                }
                        break;
                    case RowKind.Beam:
                        if (Touches(0.85, 1.15) && Y < 0.75f)
                            damage = 0.1f;
                        break;
                    case RowKind.Lava:
                        if (Touches(0.15, 1.85) && Y < 0.05f)
                            damage = 0.05f;
                        break;
                    // The ghost is not collected: a surge or the grace after a hit passes through it, and the
                    // one that takes the run stays over the fallen runner.
                    case RowKind.Power when kind.Power == GHOST:
                        if (Touches(0.5, 1.5) && Near(kind.Lane, 0.9f) && Y < 1.7f)
                            damage = 0.3f;
                        break;
                    case RowKind.Orb:
                    case RowKind.Power:
                        if (Touches(0.5, 1.5) && Near(kind.Lane, 0.9f) && Y < 1.7f && !_collected.Contains(i))
                        {
                            if (_collected.Count >= 64)
                                _collected.RemoveAt(0);
                            _collected.Add(i);
                            if (kind.Kind == RowKind.Power)
                            {
                                if (kind.Power == HEALTH)
                                    (Health, Heal) = (Math.Min(Health + 0.15f, 1.0f), 1.0f);
                                else if (kind.Power == SHIELD)
                                    Shield = SHIELD_SECONDS;
                                else
                                    (_surge, Warp) = (4.0f, 1.0f);
                                _pickup = kind.Power;
                            }
                            else
                            {
                                TakeOrb(i);
                            }
                        }
                        break;
                }
                // The title's run cannot be hurt, a surge goes through everything.
                bool shielded = Shield > 0.0f && (kind.Kind == RowKind.Pillars || kind.IsGhost);
                if (damage > 0.0f && Phase == Phase.Playing && _invulnerable <= 0.0f && _surge <= 0.0f && shielded)
                {
                    // The shield takes a pillar or a ghost: nothing lost, no stumble, a golden burst.
                    (Shield, ShieldBurst, _invulnerable, Shake) = (0.0f, 1.0f, 0.8f, 0.4f);
                }
                else if (damage > 0.0f && Phase == Phase.Playing && _invulnerable <= 0.0f && _surge <= 0.0f)
                {
                    Health -= damage;
                    Hits += 1;
                    (Flash, Shake, _invulnerable) = (0.4f + damage * 2.0f, 0.5f + damage * 2.5f, 0.8f);
                    if (kind.IsGhost)
                    {
                        // The ghost: a blackout, no red flash. When it takes the last of the health it stays in
                        // front of the camera, over the runner going down.
                        (Ghost, Flash, Shake, Warp) = (1.0f, 0.0f, 1.2f, 1.0f);
                        if (Health < 0.005f)
                        {
                            Killer = i;
                            (Z, _lane, X, _pace) = (z0 - 2.2, kind.Lane, kind.Lane * LANE, 0.0f);
                        }
                        else
                        {
                            _pace = 0.6f;
                        }
                    }
                    else if (kind.Kind == RowKind.Pillars)
                    {
                        // A pillar stops the run: thrown back in front of it and aside into the nearest free
                        // lane, then up to speed again.
                        _pace = 0.0f;
                        Z = z0 - 0.7;
                        int? free = null;
                        for (int lane = -1; lane <= 1; lane++)
                            if ((kind.Mask & LaneBit(lane)) == 0 && (free == null || Math.Abs(lane - _lane) < Math.Abs(free.Value - _lane)))
                                free = lane; // the first of equally near lanes, as Rust's min_by_key
                        if (free != null)
                            _lane = free.Value;
                    }
                    else
                    {
                        _pace = 0.6f;
                    }
                    if (Health < 0.005f)
                        Die();
                    if (_pace == 0.0f)
                        break;
                }
            }
        }

        long zone = (long)Math.Floor(Z / ZONE);
        if (zone != _zone)
            (_zone, Warp) = (zone, 1.0f);
    }

    /// <summary>
    /// The title's player: follows the orbs (their lane is the one the next pillars leave free) and jumps what must
    /// be jumped.
    /// </summary>
    internal Input Pilot(float dt)
    {
        var input = new Input();
        _pilotWait -= dt;
        // Inside an event row it still wants the lane that row leaves free.
        long group = DivEuclid((long)Math.Floor(Z / 2.0) - 1, 5);
        long evt = (group + 1) * 5;
        float distance = (float)(evt * 2.0 + 1.0 - Z);
        if (GetRow(evt).IsJump && distance > 0.0f && distance < Speed * 0.36f + 0.3f)
            input.Jump = true;
        int want = OrbLane(group);
        // A ghost, or a shield it has no use for, in the wanted lane: the next lane over until it is passed.
        long avoid = group * 5 + 3;
        var row = GetRow(avoid);
        if (row.Kind == RowKind.Power && (row.Power == GHOST || row.Power == SHIELD) && row.Lane == want && Z < avoid * 2.0 + 1.5)
            want = want == 1 ? 0 : want + 1;
        if (want != _lane && _pilotWait <= 0.0f)
        {
            (input.Left, input.Right) = (want < _lane, want > _lane);
            _pilotWait = 0.16f;
        }
        return input;
    }

    /// <summary>The color of the torches where the runner is: the light of the zone.</summary>
    public void Light(Span<float> rgb) => Palette(0, rgb);

    /// <summary>
    /// The orb flash at the screen's borders: the color of the orbs where the runner is (the zone's accent, channels
    /// turned: uAccent.gbr) with as much light as the first flash (the sum of its channels).
    /// </summary>
    public void OrbFlash(Span<float> rgb)
    {
        Span<float> accent = stackalloc float[3];
        Palette(1, accent);
        float c0 = accent[1], c1 = accent[2], c2 = accent[0];
        float k = (FIRST_FLASH[0] + FIRST_FLASH[1] + FIRST_FLASH[2]) / Math.Max(c0 + c1 + c2, 0.01f);
        rgb[0] = c0 * k;
        rgb[1] = c1 * k;
        rgb[2] = c2 * k;
    }

    /// <summary>Torch (0), accent (1) or fog (2) of where the runner is; the last tenth of a zone turns into the next.</summary>
    public void Palette(int which, Span<float> rgb)
    {
        double at = Z / ZONE;
        double zone = Math.Floor(at);
        float t = Math.Clamp(((float)(at - zone) - 0.9f) / 0.1f, 0.0f, 1.0f);
        var a = PALETTES[(int)((ulong)zone % (ulong)PALETTES.Length)][which];
        var b = PALETTES[(int)(((ulong)zone + 1) % (ulong)PALETTES.Length)][which];
        for (int j = 0; j < 3; j++)
            rgb[j] = a[j] + (b[j] - a[j]) * t;
    }
}
