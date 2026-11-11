global using DrawnUi;
global using DrawnUi.Draw;
global using DrawnUi.Views;
global using DrawnUi.Controls;
global using DrawnUi.Gaming;
global using DrawnUi.Infrastructure.Enums;
global using AppoMobi.Gestures;
global using SkiaSharp;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using DrawnUi.Testing;

namespace DungeonRun;

/// <summary>
/// Renders the game headless and reports per frame: managed bytes allocated, time, and the types the allocations
/// were sampled in (GCAllocationTick, one sample per ~100 KB). Usage: Dungeon.Profile [frames] [run].
/// </summary>
public static class Program
{
    public static void Main(string[] args)
    {
        int frames = args.Length > 0 ? int.Parse(args[0]) : 1200;
        bool run = args.Contains("run");
        if (args.Contains("mesh"))
        {
            MeshOnly(frames);
            return;
        }

        using var host = new HeadlessCanvasHost(240, 160, 1.0f, Colors.Black);
        var app = new DungeonApp();
        host.Canvas.Content = app.Build();
        if (args.Contains("noscene"))
        {
            // everything but the mesh and the post effect, which are slow on a raster surface (SkSL on the CPU)
            var scene = (Scene)typeof(DungeonApp).GetField("_scene", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(app);
            scene.IsVisible = false;
        }
        host.AdvanceFrames(60);
        if (run)
        {
            app.KeyDown(InputKey.Space); // start: countdown, then the run
            host.AdvanceFrames(240);
        }

        using var listener = new AllocationListener();
        long before = GC.GetAllocatedBytesForCurrentThread();
        var gc0 = GC.CollectionCount(0);
        var watch = Stopwatch.StartNew();
        for (int i = 0; i < frames; i++)
        {
            if (run && i % 30 == 0)
                app.KeyDown(InputKey.ArrowUp); // keep jumping now and then, the pilot is off in a run
            host.RenderFrame(16.0);
        }
        watch.Stop();
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Console.WriteLine($"{(run ? "run" : "title")}: {frames} frames, {bytes / frames} B/frame, {watch.Elapsed.TotalMilliseconds / frames:F2} ms/frame, gen0 {GC.CollectionCount(0) - gc0}");
        foreach (var (type, count) in listener.Types.OrderByDescending(x => x.Value).Take(40))
            Console.WriteLine($"  {count,5} x ~100KB  {type}");
    }

    /// <summary>
    /// The SKMesh calls of Scene alone, a mesh of ~490 triangles rebuilt every frame (count varying as the corridor
    /// does) and drawn on a tiny raster surface, so the allocations of the mesh path show without the pixel cost.
    /// </summary>
    static void MeshOnly(int frames)
    {
        using DrawnUi.Draw.Meshes.SKMeshSpecification spec = DrawnUi.Draw.Meshes.SKMeshSpecification.Make(
            [
                new(DrawnUi.Draw.Meshes.SKMeshSpecificationAttributeType.Float2, 0, "position"),
                new(DrawnUi.Draw.Meshes.SKMeshSpecificationAttributeType.Float4, 8, "a"),
                new(DrawnUi.Draw.Meshes.SKMeshSpecificationAttributeType.Float4, 24, "b"),
                new(DrawnUi.Draw.Meshes.SKMeshSpecificationAttributeType.Float4, 40, "c"),
            ], 56,
            [
                new(DrawnUi.Draw.Meshes.SKMeshSpecificationVaryingType.Float4, "a"),
                new(DrawnUi.Draw.Meshes.SKMeshSpecificationVaryingType.Float4, "b"),
                new(DrawnUi.Draw.Meshes.SKMeshSpecificationVaryingType.Float4, "c"),
            ], Shaders.MESH_VS, Shaders.MESH_FS, SKColorSpace.CreateSrgb(), SKAlphaType.Premul, out var errors);
        if (spec == null)
            throw new Exception(errors);
        var floats = new float[3000 * 3 * 14];
        var rnd = new Random(1);
        for (int i = 0; i < floats.Length; i++)
            floats[i] = (float)rnd.NextDouble() * 4 + 0.5f;
        var uniforms = new float[20];
        using var surface = SKSurface.Create(new SKImageInfo(4, 4));
        using var paint = new SKPaint { Color = SKColors.White };

        void Frame(int i)
        {
            int count = (480 + i * 7 % 40) * 3;
            using var buffer = DrawnUi.Draw.Meshes.SKMeshVertexBuffer.Make(
                System.Runtime.InteropServices.MemoryMarshal.AsBytes(floats.AsSpan(0, count * 14)));
            using var data = SKData.CreateCopy(System.Runtime.InteropServices.MemoryMarshal.AsBytes(uniforms.AsSpan()));
            using var mesh = DrawnUi.Draw.Meshes.SKMesh.Make(spec, DrawnUi.Draw.Meshes.SKMeshMode.Triangles, buffer, count, 0, data,
                new SKRect(0, 0, 4, 4), out _);
            DrawnUi.Draw.Meshes.SKMeshCanvasExtensions.DrawMesh(surface.Canvas, mesh, paint);
        }

        for (int i = 0; i < 200; i++)
            Frame(i);
        using var listener = new AllocationListener();
        long before = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        for (int i = 0; i < frames; i++)
            Frame(i);
        watch.Stop();
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Console.WriteLine($"mesh: {frames} frames, {bytes / frames} B/frame, {watch.Elapsed.TotalMilliseconds / frames:F3} ms/frame");
        foreach (var (type, count) in listener.Types.OrderByDescending(x => x.Value).Take(30))
            Console.WriteLine($"  {count,5} x ~100KB  {type}");
    }

    sealed class AllocationListener : EventListener
    {
        public readonly Dictionary<string, int> Types = new();

        protected override void OnEventSourceCreated(EventSource source)
        {
            if (source.Name == "Microsoft-Windows-DotNETRuntime")
                EnableEvents(source, EventLevel.Verbose, (EventKeywords)0x1);
        }

        protected override void OnEventWritten(EventWrittenEventArgs e)
        {
            if (e.EventName == null || !e.EventName.StartsWith("GCAllocationTick"))
                return;
            int index = e.PayloadNames.IndexOf("TypeName");
            if (index < 0)
                return;
            var type = e.Payload[index] as string ?? "?";
            lock (Types)
                Types[type] = Types.TryGetValue(type, out var n) ? n + 1 : 1;
        }
    }
}
