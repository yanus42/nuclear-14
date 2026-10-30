namespace Content.Client._Forge.Humanoid;

// Holds only the latest recipe until a visible portrait actually needs an entity.
public sealed class AppearancePreviewDoll<T>(Action<T> delete) where T : struct
{
    private Func<T>? _create;
    private Action<T>? _update;
    public T? Entity { get; private set; }
    public bool HasDefinition => _create != null;

    public void Configure(Func<T> create, Action<T> update)
    {
        _create = create;
        _update = update;
    }

    public void Apply()
    {
        if (Entity is { } entity) _update?.Invoke(entity);
        else if (_create != null) Entity = _create();
    }

    public void Release()
    {
        if (Entity is not { } entity) return;
        Entity = null;
        delete(entity);
    }

    public void Clear()
    {
        Release();
        _create = null;
        _update = null;
    }
}
