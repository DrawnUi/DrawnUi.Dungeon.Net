// The drawn dungeon (port of examples/dungeon/src/scene.rs, the `Scene` and `Builder` half). Every frame the
// visible quads are clipped at the near plane and projected on the CPU into ONE SKMesh; its fragment program
// does the materials, the torches and the fog. The game loop steps `World` and marks the control to draw again.

using System.Runtime.InteropServices;
using DrawnUi.Draw.Meshes;

namespace DungeonRun;

public class Scene : SkiaControl
{
    /// <summary>Segments drawn ahead; the fog hides the end.</summary>
    const long VISIBLE = 50;
    const float NEAR = 0.2f;
    /// <summary>
    /// The shader works in coordinates moved back by a multiple of this, so they stay small. A multiple of two
    /// torch distances: the torches keep their walls.
    /// </summary>
    const double REBASE = 40.0;
    /// <summary>Floats per vertex: position 2, then three float4.</summary>
    const int FLOATS = 14;

    const float W = World.HALF_WIDTH;
    const float H = World.HEIGHT;

    public readonly World World = new();

    SKMeshSpecification _spec;
    bool _specMade;
    bool _reported;

    /// <summary>The frame's vertices, kept for their allocation.</summary>
    float[] _vertices = new float[4096 * FLOATS];
    int _floats;

    readonly float[] _uniforms = new float[20];
    readonly float[] _palette = new float[9];
    readonly SKPaint _paint = new();

    // The builder of the frame (Rust `Builder`): camera in the shader's rebased coordinates, sideways and upward
    // bend per squared unit of depth, sine and cosine of the camera roll, screen center, focal length, bounds.
    float _camX, _camY, _camZ;
    float _bendX, _bendY;
    float _rollSin, _rollCos;
    float _centerX, _centerY, _focal;
    float _left, _top, _right, _bottom;
    // Per corner: view xyz, world xyz, uv, glow; and the near-clipped polygon (at most 6 corners).
    readonly float[] _view = new float[4 * 9];
    readonly float[] _polygon = new float[6 * 9];
    readonly float[] _projected = new float[6 * FLOATS];

    public Scene()
    {
        UseCache = SkiaCacheType.None; // draws every frame; the post effect reads what it drew
    }

    /// <summary>One line per problem, not one per frame.</summary>
    void Report(string what, string error)
    {
        if (_reported)
            return;
        _reported = true;
        Console.WriteLine($"dungeon: {what}: {error}");
    }


    protected override void Paint(DrawingContext ctx)
    {
        base.Paint(ctx);

        if (!_specMade)
        {
            _specMade = true;
            ReadOnlySpan<SKMeshSpecificationAttribute> attributes =
            [
                new(SKMeshSpecificationAttributeType.Float2, 0, "position"),
                new(SKMeshSpecificationAttributeType.Float4, 8, "a"),
                new(SKMeshSpecificationAttributeType.Float4, 24, "b"),
                new(SKMeshSpecificationAttributeType.Float4, 40, "c"),
            ];
            ReadOnlySpan<SKMeshSpecificationVarying> varyings =
            [
                new(SKMeshSpecificationVaryingType.Float4, "a"),
                new(SKMeshSpecificationVaryingType.Float4, "b"),
                new(SKMeshSpecificationVaryingType.Float4, "c"),
            ];
            _spec = SKMeshSpecification.Make(attributes, FLOATS * 4, varyings, Shaders.MESH_VS, Shaders.MESH_FS,
                SKColorSpace.CreateSrgb(), SKAlphaType.Premul, out var errors);
            if (_spec == null)
                Report("mesh SkSL", errors);
        }
        if (_spec == null)
            return;

        var world = World;
        var rect = ctx.Destination;
        var canvas = ctx.Context.Canvas;
        Span<float> torch = _palette.AsSpan(0, 3), accent = _palette.AsSpan(3, 3), fog = _palette.AsSpan(6, 3);
        world.Palette(0, torch);
        world.Palette(1, accent);
        world.Palette(2, fog);
        // A ghost near: the torches dim, the fog thickens and darkens. The exit's daylight: from 600 units before
        // it, the whole corridor brightens toward it (ambient, torches, the far end), the walls and the floor catch
        // the light from the door; stronger out of the door.
        float dim = 1.0f - 0.55f * world.Dread;
        float exitLight = (float)Math.Clamp((world.Z - (World.EXIT - 600.0)) / 600.0, 0.0, 1.0) * (1.0f + 0.8f * world.Escape);
        float far = Math.Min(exitLight * 0.9f, 1.0f);
        const float sky0 = 1.0f, sky1 = 0.9f, sky2 = 0.7f;
        fog[0] = fog[0] * dim * (1.0f - far) + sky0 * far;
        fog[1] = fog[1] * dim * (1.0f - far) + sky1 * far;
        fog[2] = fog[2] * dim * (1.0f - far) + sky2 * far;

        // What no quad covers is the fog at the end of the corridor (the sky past the exit): the same color the
        // shader fogs to, so the last drawn row has no seam.
        _paint.Color = new SKColor(ToByte(fog[0]), ToByte(fog[1]), ToByte(fog[2]), 255);
        canvas.DrawRect(rect, _paint);

        _floats = 0;
        double baseZ = Math.Floor(world.Z / REBASE) * REBASE;
        float onGround = world.Y <= 0.0f ? 1.0f : 0.0f;
        _camX = world.X * 0.92f + MathF.Sin(world.Time * 90.0f) * 0.12f * world.Shake;
        // Going down, the camera sinks to the floor and tips over.
        _camY = 1.55f - 1.2f * world.Fall * world.Fall + world.Y * 0.9f + MathF.Sin((float)world.Z * 1.1f) * 0.05f * onGround
                + MathF.Cos(world.Time * 70.0f) * 0.1f * world.Shake;
        _camZ = (float)(world.Z - baseZ);
        float d = (float)world.Z;
        // The corridor straightens over 600 to 300 units before the exit: from there the way out is dead ahead,
        // and the long stretch to it is drawn as straight quads.
        float straight = (float)Math.Clamp((World.EXIT - World.EXIT_SIGHT - world.Z) / 300.0, 0.0, 1.0);
        _bendX = (0.0032f * MathF.Sin(d * 0.011f) + 0.0016f * MathF.Sin(d * 0.027f + 1.3f)) * straight;
        _bendY = (0.0018f * MathF.Sin(d * 0.007f + 0.5f) - 0.0006f) * straight;
        float roll = world.Lean + _bendX * 18.0f + 0.6f * world.Fall * world.Fall;
        (_rollSin, _rollCos) = (MathF.Sin(roll), MathF.Cos(roll));
        _centerX = rect.MidX;
        _centerY = rect.MidY;
        _focal = Math.Min(rect.Height * 0.5f, rect.Width * 0.75f);
        (_left, _top, _right, _bottom) = (float.MaxValue, float.MaxValue, float.MinValue, float.MinValue);
        Corridor(world, baseZ);

        int count = _floats / FLOATS;
        if (count == 0)
            return;
        var bytes = MemoryMarshal.AsBytes(_vertices.AsSpan(0, _floats));
        using var buffer = SKMeshVertexBuffer.Make(bytes);
        if (buffer == null)
            return;
        float bright = dim * (1.0f + 0.7f * exitLight);
        // Near the exit the air clears (daylight), so the opening reads from far.
        float ramp = (float)Math.Clamp((world.Z - (World.EXIT - 600.0)) / 600.0, 0.0, 1.0);
        var u = _uniforms;
        (u[0], u[1], u[2], u[3]) = (world.Time, _camX, _camY, _camZ);
        (u[4], u[5], u[6], u[7]) = (torch[0] * bright, torch[1] * bright, torch[2] * bright, (float)(World.EXIT - baseZ));
        (u[8], u[9], u[10], u[11]) = (accent[0], accent[1], accent[2], exitLight);
        (u[12], u[13], u[14], u[15]) = (fog[0], fog[1], fog[2], 0.028f * (1.0f - 0.4f * ramp) + 0.08f * world.Dread);
        (u[16], u[17], u[18], u[19]) = (world.Glare, 0.0f, 0.0f, 0.0f);
        using var uniforms = SKData.CreateCopy(MemoryMarshal.AsBytes(u.AsSpan()));
        using var mesh = SKMesh.Make(_spec, SKMeshMode.Triangles, buffer, count, 0, uniforms,
            new SKRect(_left, _top, _right, _bottom), out var meshErrors);
        if (mesh == null)
        {
            Report("mesh", meshErrors);
            return;
        }
        // The mesh color is multiplied by the paint color.
        _paint.Color = SKColors.White;
        canvas.Save();
        canvas.ClipRect(rect);
        canvas.DrawMesh(mesh, _paint);
        canvas.Restore();
    }

    static byte ToByte(float channel) => (byte)(Math.Clamp(channel, 0.0f, 1.0f) * 255.0f);

    // ---- the builder ----

    /// <summary>A quad given by its corners (uv 0,0 / 1,0 / 1,1 / 0,1): clipped at the near plane, projected.</summary>
    void Quad(float ax, float ay, float az, float bx, float by, float bz, float cx, float cy, float cz, float dx, float dy, float dz,
        float material, float g0, float g1, float g2, float g3, float nx, float ny, float nz, float seed)
    {
        var view = _view;
        Corner(view, 0, ax, ay, az, 0.0f, 0.0f, g0);
        Corner(view, 1, bx, by, bz, 1.0f, 0.0f, g1);
        Corner(view, 2, cx, cy, cz, 1.0f, 1.0f, g2);
        Corner(view, 3, dx, dy, dz, 0.0f, 1.0f, g3);
        if (view[2] < NEAR && view[9 + 2] < NEAR && view[18 + 2] < NEAR && view[27 + 2] < NEAR)
            return;
        var polygon = _polygon;
        int n = 0;
        for (int k = 0; k < 4; k++)
        {
            int a = k * 9, b = (k + 1) % 4 * 9;
            bool aIn = view[a + 2] >= NEAR, bIn = view[b + 2] >= NEAR;
            if (aIn)
            {
                Array.Copy(view, a, polygon, n * 9, 9);
                n++;
            }
            if (aIn != bIn)
            {
                float t = (NEAR - view[a + 2]) / (view[b + 2] - view[a + 2]);
                for (int j = 0; j < 9; j++)
                    polygon[n * 9 + j] = view[a + j] + (view[b + j] - view[a + j]) * t;
                n++;
            }
        }
        if (n < 3)
            return;
        // Each corner of the polygon projected once, then the fan's triangles copied from them.
        var projected = _projected;
        for (int k = 0; k < n; k++)
            Project(k * 9, k * FLOATS, material, nx, ny, nz, seed);
        int triangles = n - 2;
        if (_floats + triangles * 3 * FLOATS > _vertices.Length)
            Array.Resize(ref _vertices, Math.Max(_vertices.Length * 2, _floats + triangles * 3 * FLOATS));
        var vertices = _vertices;
        for (int k = 1; k < n - 1; k++)
        {
            Array.Copy(projected, 0, vertices, _floats, FLOATS);
            Array.Copy(projected, k * FLOATS, vertices, _floats + FLOATS, 2 * FLOATS);
            _floats += 3 * FLOATS;
        }
    }

    void Corner(float[] view, int k, float px, float py, float pz, float u, float v, float glow)
    {
        float dx = px - _camX, dy = py - _camY, dz = pz - _camZ;
        float ahead = dz > 0.0f ? dz : 0.0f;
        float depth = ahead * ahead;
        float bx = dx + _bendX * depth, by = dy + _bendY * depth;
        int o = k * 9;
        view[o] = bx * _rollCos - by * _rollSin;
        view[o + 1] = bx * _rollSin + by * _rollCos;
        view[o + 2] = dz;
        view[o + 3] = px;
        view[o + 4] = py;
        view[o + 5] = pz;
        view[o + 6] = u;
        view[o + 7] = v;
        view[o + 8] = glow;
    }

    /// <summary>Polygon corner <paramref name="at"/> projected into the vertex layout at <paramref name="to"/> of the scratch.</summary>
    void Project(int at, int to, float material, float nx, float ny, float nz, float seed)
    {
        var p = _polygon;
        var o = _projected;
        float w = 1.0f / p[at + 2];
        float x = _centerX + _focal * p[at] * w, y = _centerY - _focal * p[at + 1] * w;
        if (x < _left) _left = x;
        if (y < _top) _top = y;
        if (x > _right) _right = x;
        if (y > _bottom) _bottom = y;
        o[to] = x;
        o[to + 1] = y;
        o[to + 2] = p[at + 3] * w;
        o[to + 3] = p[at + 4] * w;
        o[to + 4] = p[at + 5] * w;
        o[to + 5] = w;
        o[to + 6] = p[at + 6] * w;
        o[to + 7] = p[at + 7] * w;
        o[to + 8] = material;
        o[to + 9] = p[at + 8] * w;
        o[to + 10] = nx;
        o[to + 11] = ny;
        o[to + 12] = nz;
        o[to + 13] = seed;
    }
    /// <summary>A sprite facing the run.</summary>
    void Sprite(float x, float y0, float y1, float z, float halfWidth, float material, float seed)
    {
        float x0 = x - halfWidth, x1 = x + halfWidth;
        Quad(x0, y0, z, x1, y0, z, x1, y1, z, x0, y1, z, material, 0, 0, 0, 0, 0.0f, 0.0f, -1.0f, seed);
    }

    /// <summary>Far to near: each segment's floor, ceiling and walls, then what stands in it.</summary>
    void Corridor(World world, double baseZ)
    {
        long first = (long)Math.Floor(world.Z / 2.0);
        // The way out, once in sight: the corridor simply ends, open. Behind the opening, the world outside (one
        // wide quad far off: sky, sun, clouds, mountains in the shader) and the stone floor going on as a terrace;
        // the stretch of corridor past the drawn rows up to the door as four long quads (straight by then).
        double door = World.EXIT_ROW * 2.0 - baseZ;
        if (world.Z >= World.EXIT - World.EXIT_SIGHT)
        {
            float sky = (float)(door + 90.0);
            Quad(-400, -40, sky, 400, -40, sky, 400, 200, sky, -400, 200, sky, 13.0f, 0, 0, 0, 0, 0.0f, 0.0f, -1.0f, 0.0f);
            float t0 = (float)door, t1 = (float)(door + 24.0);
            Quad(-30, 0, t0, 30, 0, t0, 30, 0, t1, -30, 0, t1, 0.0f, 0, 0, 0, 0, 0.0f, 1.0f, 0.0f, 0.0f);
            long nearRow = first + VISIBLE;
            if (nearRow < World.EXIT_ROW)
            {
                float z0 = (float)(nearRow * 2.0 - baseZ), z1 = (float)door;
                Quad(-W, 0, z0, W, 0, z0, W, 0, z1, -W, 0, z1, 0.0f, 0, 0, 0, 0, 0.0f, 1.0f, 0.0f, 0.0f);
                Quad(-W, H, z0, W, H, z0, W, H, z1, -W, H, z1, 1.0f, 0, 0, 0, 0, 0.0f, -1.0f, 0.0f, 0.0f);
                Quad(-W, 0, z0, -W, 0, z1, -W, H, z1, -W, H, z0, 2.0f, 0, 0, 0, 0, 1.0f, 0.0f, 0.0f, 0.0f);
                Quad(W, 0, z0, W, 0, z1, W, H, z1, W, H, z0, 2.0f, 0, 0, 0, 0, -1.0f, 0.0f, 0.0f, 0.0f);
            }
        }
        // the glow at a segment's far end is the one at the near end of the segment drawn before it
        float glowAfter = World.LavaGlow(first + VISIBLE);
        for (long i = first + VISIBLE - 1; i >= first; i--)
        {
            float g1 = glowAfter, g0 = glowAfter = World.LavaGlow(i);
            if (i >= World.EXIT_ROW)
                continue;
            float z0 = (float)(i * 2.0 - baseZ);
            float z1 = z0 + World.SEG;
            var kind = World.GetRow(i);
            float floor = kind.Kind == RowKind.Lava ? 3.0f : 0.0f;
            Quad(-W, 0, z0, W, 0, z0, W, 0, z1, -W, 0, z1, floor, g0 * 0.6f, g0 * 0.6f, g1 * 0.6f, g1 * 0.6f, 0.0f, 1.0f, 0.0f, 0.0f);
            Quad(-W, H, z0, W, H, z0, W, H, z1, -W, H, z1, 1.0f, g0 * 0.06f, g0 * 0.06f, g1 * 0.06f, g1 * 0.06f, 0.0f, -1.0f, 0.0f, 0.0f);
            Quad(-W, 0, z0, -W, 0, z1, -W, H, z1, -W, H, z0, 2.0f, g0, g1, g1 * 0.1f, g0 * 0.1f, 1.0f, 0.0f, 0.0f, 0.0f);
            Quad(W, 0, z0, W, 0, z1, W, H, z1, W, H, z0, 2.0f, g0, g1, g1 * 0.1f, g0 * 0.1f, -1.0f, 0.0f, 0.0f, 0.0f);

            // A torch every fifth segment, on alternating walls (the shader lights from the same places).
            if (World.RemEuclid(i, 5) == 0)
            {
                long k = World.DivEuclid(i, 5);
                float side = World.RemEuclid(k, 2) == 0 ? 1.0f : -1.0f;
                Sprite(side * 2.62f, 2.5f, 4.3f, z0 + 0.05f, 0.75f, 6.0f, World.Hash01(k, 3));
            }
            switch (kind.Kind)
            {
                case RowKind.Pillars:
                    Pillars(i, kind.Mask, z0);
                    break;
                case RowKind.Beam:
                    Sprite(0.0f, 0.1f, 1.0f, z0 + 1.0f, W, 7.0f, 0.0f);
                    break;
                case RowKind.Power when kind.Power == World.GHOST:
                {
                    // Far: a faint shimmer. Under a second away it reveals itself (`reveal`, 0 to 1: eyes open, it
                    // swells and lunges at the camera). The one that took the run leans in over the fallen camera
                    // (1 + fall). The glow slot carries the value.
                    float distance = (float)(i * 2.0 + 1.0 - world.Z);
                    float reveal = Math.Clamp(1.0f - distance / (Math.Max(world.Speed, 8.0f) * 0.9f), 0.0f, 1.0f);
                    float state, lunge, loom;
                    if (world.Killer == i)
                        (state, lunge, loom) = (1.0f + world.Fall, 0.0f, world.Fall);
                    else
                        (state, lunge, loom) = (reveal, reveal * reveal, 0.0f);
                    float y = 1.35f + 0.1f * MathF.Sin(world.Time * 1.3f + i) - 0.3f * lunge - 0.75f * loom;
                    float half = 1.0f + 0.5f * lunge + 0.2f * loom;
                    float x0 = kind.Lane * World.LANE - half, x1 = kind.Lane * World.LANE + half;
                    float z = z0 + 1.0f - 0.6f * lunge;
                    Quad(x0, y - half, z, x1, y - half, z, x1, y + half, z, x0, y + half, z, 10.0f, state, state, state, state,
                        0.0f, 0.0f, -1.0f, World.Hash01(i, 5));
                    break;
                }
                case RowKind.Power when !world.Collected(i):
                {
                    float y = 1.05f + 0.12f * MathF.Sin(world.Time * 3.0f + i);
                    float material = kind.Power == World.SHIELD ? 14.0f : 8.0f + kind.Power;
                    Sprite(kind.Lane * World.LANE, y - 0.75f, y + 0.75f, z0 + 1.0f, 0.75f, material, World.Hash01(i, 5));
                    break;
                }
                case RowKind.Orb when !world.Collected(i):
                {
                    float y = 1.0f + 0.12f * MathF.Sin(world.Time * 3.0f + i);
                    Sprite(kind.Lane * World.LANE, y - 0.6f, y + 0.6f, z0 + 1.0f, 0.6f, 5.0f, World.Hash01(i, 5));
                    break;
                }
            }
        }
    }

    /// <summary>The pillars of a row, the one farthest to the side first.</summary>
    void Pillars(long i, byte mask, float z0)
    {
        // lanes -1, 0, 1 sorted by their sideways distance from the camera, farthest first (a stable sort, as Rust's)
        Span<int> lanes = stackalloc int[] { -1, 0, 1 };
        for (int a = 1; a < 3; a++)
            for (int b = a; b > 0 && Offset(lanes[b]) > Offset(lanes[b - 1]); b--)
                (lanes[b], lanes[b - 1]) = (lanes[b - 1], lanes[b]);
        foreach (var lane in lanes)
        {
            if ((mask & (1 << (lane + 1))) == 0)
                continue;
            float x0 = lane * World.LANE - 0.85f, x1 = lane * World.LANE + 0.85f;
            float za = z0 + 0.4f, zb = z0 + 1.6f;
            float seed = World.Hash01(i * 4 + lane, 4);
            if (_camX < x0)
                Quad(x0, 0, zb, x0, 0, za, x0, H, za, x0, H, zb, 4.0f, 0, 0, 0, 0, -1.0f, 0.0f, 0.0f, seed);
            if (_camX > x1)
                Quad(x1, 0, za, x1, 0, zb, x1, H, zb, x1, H, za, 4.0f, 0, 0, 0, 0, 1.0f, 0.0f, 0.0f, seed);
            Quad(x0, 0, za, x1, 0, za, x1, H, za, x0, H, za, 4.0f, 0, 0, 0, 0, 0.0f, 0.0f, -1.0f, seed);
        }
    }

    float Offset(int lane) => Math.Abs(lane * World.LANE - _camX);

    public override void OnDisposing()
    {
        _spec?.Dispose();
        _paint.Dispose();
        base.OnDisposing();
    }
}
