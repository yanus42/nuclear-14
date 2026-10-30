namespace Content.Client._Forge.Humanoid;

// Shared frame budget across all portrait controls, independent of catalogue size.
public sealed class AppearancePreviewQueue
{
    private static uint _frame;
    private static int _applied;
    private Action? _pending;
    private TimeSpan _due;

    public bool HasPending => _pending != null;
    public void Cancel() => _pending = null;

    public void Enqueue(TimeSpan now, Action apply, bool immediate = false)
    {
        _pending = apply;
        _due = now + TimeSpan.FromSeconds(immediate ? 0 : 0.12);
    }

    public bool Update(uint frame, TimeSpan now, bool visible)
    {
        if (_frame != frame) { _frame = frame; _applied = 0; }
        if (!visible || _pending == null || now < _due || _applied >= 2) return false;
        var apply = _pending;
        _pending = null;
        _applied++;
        apply();
        return true;
    }
}
