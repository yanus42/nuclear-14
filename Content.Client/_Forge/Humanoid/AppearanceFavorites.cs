using System.Linq;

namespace Content.Client._Forge.Humanoid;

public sealed class AppearanceFavorites(Func<string?> read, Action<string> write)
{
    private HashSet<string> _items = new(StringComparer.Ordinal);
    public bool Contains(string id) => _items.Contains(id);
    public void Refresh() => _items = Parse(read());
    public void Toggle(string id)
    {
        var next = Parse(read());
        if (!next.Add(id)) next.Remove(id);
        write(string.Join("\n", next.OrderBy(value => value, StringComparer.Ordinal)));
        _items = next;
    }
    private static HashSet<string> Parse(string? text) => new(
        (text ?? "").Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);
}
