using Content.Shared.Audio.Jukebox;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;

namespace Content.Client.Audio.Jukebox;

public sealed partial class JukeboxMenu
{
    public event Action<byte>? OnVolumeChanged;
    public event Action<ProtoId<JukeboxPrototype>>? OnEnqueuePressed;
    public event Action<int, int, int>? OnQueueEditPressed;
    public event Action? OnNextPressed;

    private JukeboxPrototype[] _catalog = Array.Empty<JukeboxPrototype>();
    private ProtoId<JukeboxPrototype>? _catalogSelection;
    private int _queueSelection = -1;
    private int _queueRevision = -1;

    private void InitializePlaybackControls()
    {
        Search.OnTextChanged += _ => PopulateCatalog();
        AddQueueButton.OnPressed += _ =>
        {
            if (_catalogSelection is { } song)
                OnEnqueuePressed?.Invoke(song);
        };
        QueueList.OnItemSelected += args =>
        {
            _queueSelection = args.ItemIndex;
            UpdateQueueButtons();
        };
        RemoveQueueButton.OnPressed += _ => OnQueueEditPressed?.Invoke(_queueSelection, 0, _queueRevision);
        QueueUpButton.OnPressed += _ => OnQueueEditPressed?.Invoke(_queueSelection, -1, _queueRevision);
        QueueDownButton.OnPressed += _ => OnQueueEditPressed?.Invoke(_queueSelection, 1, _queueRevision);
        NextButton.OnPressed += _ => OnNextPressed?.Invoke();
        VolumeSlider.OnValueChanged += _ => UpdateVolumeLabel();
        VolumeSlider.OnReleased += _ => OnVolumeChanged?.Invoke((byte) Math.Clamp(MathF.Round(VolumeSlider.Value), 0f, JukeboxComponent.MaxVolumePercent));
        UpdateQueueButtons();
    }

    public void SetVolume(byte volumePercent)
    {
        if (VolumeSlider.Grabbed)
            return;

        VolumeSlider.SetValueWithoutEvent(volumePercent);
        UpdateVolumeLabel();
    }

    private void UpdateVolumeLabel()
    {
        VolumeLabel.Text = $"{MathF.Round(VolumeSlider.Value)}%";
    }

    private void PopulateCatalog()
    {
        MusicList.Clear();
        _catalogSelection = null;
        AddQueueButton.Disabled = true;
        foreach (var song in _catalog)
        {
            if (song.Name.Contains(Search.Text, StringComparison.CurrentCultureIgnoreCase))
                MusicList.AddItem(song.Name, metadata: song.ID);
        }
    }

    public void SetQueue(JukeboxComponent jukebox, IPrototypeManager prototypes)
    {
        if (_queueRevision == jukebox.QueueRevision)
            return;

        _queueRevision = jukebox.QueueRevision;
        _queueSelection = -1;
        QueueList.Clear();
        foreach (var songId in jukebox.Queue)
            QueueList.AddItem(prototypes.TryIndex(songId, out var song) ? song.Name : songId.Id);

        UpdateQueueButtons();
    }

    private void UpdateQueueButtons()
    {
        RemoveQueueButton.Disabled = _queueSelection < 0;
        QueueUpButton.Disabled = _queueSelection <= 0;
        QueueDownButton.Disabled = _queueSelection < 0 || _queueSelection >= QueueList.Count - 1;
        NextButton.Disabled = QueueList.Count == 0;
    }
}
