using Robust.Shared.Timing;
using System.Numerics;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._Forge.Humanoid;

/// <summary>
/// Frames the upper central 16x16 pixels of a standard 32x32 humanoid sprite.
/// This is the head region used by Goob's hair tiles (see NOTICE.txt).
/// </summary>
public sealed class HairPortraitView : SpriteView
{
    private TransformSystem? _transform;
    private readonly IGameTiming _timing = IoCManager.Resolve<IGameTiming>();
    private readonly AppearancePreviewQueue _queue = new();
    private readonly AppearancePreviewDoll<EntityUid> _doll;
    private bool _drawn;
    private TimeSpan _lastDraw;

    public HairPortraitView()
    {
        _doll = new AppearancePreviewDoll<EntityUid>(entity =>
        {
            if (EntMan.EntityExists(entity)) EntMan.DeleteEntity(entity);
        });
    }

    public void QueueDoll(Func<EntityUid> create, Action<EntityUid> update, bool immediate = false)
    {
        _doll.Configure(create, update);
        _queue.Enqueue(_timing.RealTime, ApplyDoll, immediate);
    }

    private void ApplyDoll()
    {
        _doll.Apply();
        SetEntity(_doll.Entity);
    }

    private void ReleaseDoll()
    {
        _drawn = false;
        _queue.Cancel();
        _doll.Release();
        SetEntity((EntityUid?) null);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (_drawn)
        {
            _lastDraw = _timing.RealTime;
            if (!_queue.HasPending && _doll.Entity == null && _doll.HasDefinition)
                _queue.Enqueue(_timing.RealTime, ApplyDoll, true);
        }
        else if (_doll.Entity != null && _timing.RealTime - _lastDraw > TimeSpan.FromSeconds(0.5))
            ReleaseDoll();
        if (_queue.Update(_timing.CurFrame, _timing.RealTime, _drawn) && Entity is { } entity)
            EntMan.System<SpriteSystem>().ForceUpdate(entity.Owner);
        _drawn = false;
    }

    protected override void VisibilityChanged(bool newVisible)
    {
        base.VisibilityChanged(newVisible);
        if (!newVisible && _doll != null) ReleaseDoll();
    }

    protected override void ExitedTree()
    {
        if (_doll != null) ReleaseDoll();
        base.ExitedTree();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ReleaseDoll();
            _doll.Clear();
        }
        base.Dispose(disposing);
    }
    // Region in source sprite pixels; a stable crop across poses and marking bounds.
    public UIBox2 Region { get; set; } = new(8, 0, 24, 16);

    protected override void Draw(IRenderHandle renderHandle)
    {
        _drawn = true; // Also report an empty placeholder as visible, to request its first doll.
        if (Entity is not { } entity || EntMan.Deleted(entity.Owner))
            return;

        SpriteSystem ??= EntMan.System<SpriteSystem>();
        _transform ??= EntMan.System<TransformSystem>();
        _drawn = true;

        // Center the head crop, rather than centering the full body's bounding box.
        // Pixel dimensions include UI scaling; keep this framing independent of hair bounds.
        var zoom = MathF.Min(PixelSize.X / Region.Width, PixelSize.Y / Region.Height);
        var position = PixelSize / 2 + (new Vector2(16, 16) - Region.Center) * zoom
            - entity.Comp1.Offset * new Vector2(1, -1) * 32 * zoom;
        var world = renderHandle.DrawingHandleWorld;
        var oldModulate = world.Modulate;
        world.Modulate *= Modulate * ActualModulateSelf;
        renderHandle.DrawEntity(entity.Owner, position, new Vector2(zoom),
            WorldRotation, EyeRotation, OverrideDirection, entity.Comp1, entity.Comp2, _transform);
        world.Modulate = oldModulate;
    }
}
