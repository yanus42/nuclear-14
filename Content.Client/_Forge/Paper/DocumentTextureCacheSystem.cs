using Robust.Client.Graphics;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using Content.Shared._Forge.Paper;
using Robust.Shared.Timing;
using System.Linq;
using Content.Shared.GameTicking;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Content.Client._Forge.Paper;

internal sealed class DocumentTextures(Texture background, Texture? overlay = null) : IDisposable
{
    public readonly Texture Background = background;
    public readonly Texture? Overlay = overlay;
    public void Dispose() { (Background as IDisposable)?.Dispose(); (Overlay as IDisposable)?.Dispose(); }
}

public sealed class DocumentTextureCacheSystem : EntitySystem
{
    internal readonly DocumentResourceCache<DocumentTextures> Cache = new(64L * 1024 * 1024);
    private readonly Dictionary<string, Task<(Image<Rgba32> Background, Image<Rgba32> Overlay)>> _pending = new();
    private int _activeWorkers;
    private readonly Dictionary<string, TimeSpan> _pendingUsed = new();
    [Dependency] private readonly IGameTiming _timing = default!;
    private TimeSpan _nextSweep;
    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_timing.RealTime < _nextSweep) return;
        _nextSweep = _timing.RealTime + TimeSpan.FromSeconds(1);
        foreach (var key in _pendingUsed.Where(p => _timing.RealTime - p.Value > TimeSpan.FromSeconds(2)).Select(p => p.Key).ToArray())
        {
            DisposeWhenReady(_pending[key]); _pending.Remove(key); _pendingUsed.Remove(key);
        }
    }
    private static async void DisposeWhenReady(Task<(Image<Rgba32> Background, Image<Rgba32> Overlay)> task)
    {
        try
        {
            var images = await task;
            images.Background.Dispose(); images.Overlay.Dispose();
        }
        catch { /* A failed generation has no image buffers to release. */ }
    }
    internal DocumentResourceCache<DocumentTextures>.Lease? Surface(string key, long bytes,
        PaperSurfaceAppearance appearance, PaperSurfacePrototype profile, int width, int height, int side)
    {
        var cached = Cache.Acquire(key, 0, () => throw new InvalidOperationException());
        if (cached != null) return cached;
        if (!_pending.TryGetValue(key, out var task))
        {
            if (_pending.Count >= 4 || Interlocked.CompareExchange(ref _activeWorkers, 0, 0) >= 2) return null;
            var snapshot = appearance.Clone();
            _pendingUsed[key] = _timing.RealTime;
            Interlocked.Increment(ref _activeWorkers);
            _pending[key] = Task.Run(() =>
            {
                try { return PaperSurfaceGenerator.Generate(snapshot, profile, width, height, side); }
                finally { Interlocked.Decrement(ref _activeWorkers); }
            });
            return null;
        }
        _pendingUsed[key] = _timing.RealTime;
        if (!task.IsCompleted) return null;
        if (!task.IsCompletedSuccessfully) { _pending.Remove(key); _pendingUsed.Remove(key); return null; }
        // IsCompletedSuccessfully above guarantees this read never waits on the UI thread.
#pragma warning disable RA0004
        var images = task.Result;
#pragma warning restore RA0004
        // Texture upload is done on the UI thread; generation contains no graphics calls.
        var lease = Cache.Acquire(key, bytes, () =>
        {
            var background = Texture.LoadFromImage(images.Background, "cached paper background");
            try { return new DocumentTextures(background, Texture.LoadFromImage(images.Overlay, "cached paper creases")); }
            catch { (background as IDisposable)?.Dispose(); throw; }
        });
        if (lease != null)
        {
            _pending.Remove(key); _pendingUsed.Remove(key); images.Background.Dispose(); images.Overlay.Dispose();
        }
        return lease;
    }
    private void Clear()
    {
        foreach (var task in _pending.Values) DisposeWhenReady(task);
        _pending.Clear(); _pendingUsed.Clear(); Cache.Dispose();
    }
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => Clear());
    }
    public override void Shutdown() { Clear(); base.Shutdown(); }
    internal DocumentResourceCache<DocumentTextures>.Lease? Photo(string key, byte[]? data = null)
    {
        // Zero-byte lookup succeeds only when this resource has already been cached.
        if (data == null) return Cache.Acquire("photo:" + key, 0, () => throw new InvalidOperationException());
        var existing = Photo(key);
        if (existing != null) return existing;
        if (data.Length > 4 * 1024 * 1024) return null;
        // In-game cameras produce PNG. Read IHDR before decoding to bound allocations,
        // without Image.Identify, which is unavailable in the content sandbox.
        if (!TryPhotoSize(data, out var width, out var height)) return null;
        return Cache.Acquire("photo:" + key, (long)width * height * 4, () =>
        {
            using var stream = new MemoryStream(data, false);
            using var image = Image.Load<Rgba32>(stream);
            return new DocumentTextures(Texture.LoadFromImage(image, "cached document photograph"));
        });
    }

    internal static bool TryPhotoSize(byte[] data, out int width, out int height)
    {
        width = height = 0;
        if (data.Length < 33 || data[0] != 137 || data[1] != 80 || data[2] != 78 || data[3] != 71 ||
            data[4] != 13 || data[5] != 10 || data[6] != 26 || data[7] != 10 ||
            data[8] != 0 || data[9] != 0 || data[10] != 0 || data[11] != 13 ||
            data[12] != 73 || data[13] != 72 || data[14] != 68 || data[15] != 82)
            return false;
        width = (data[16] << 24) | (data[17] << 16) | (data[18] << 8) | data[19];
        height = (data[20] << 24) | (data[21] << 16) | (data[22] << 8) | data[23];
        return width is > 0 and <= 2048 && height is > 0 and <= 2048;
    }
}
