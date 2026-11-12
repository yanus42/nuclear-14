using Content.Shared.Audio.Jukebox;
using Content.Server.Power.EntitySystems;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server.Audio.Jukebox;

public sealed partial class JukeboxSystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    private static readonly TimeSpan VolumeChangeInterval = TimeSpan.FromMilliseconds(250);

    private void OnEnqueue(Entity<JukeboxComponent> ent, ref JukeboxEnqueueMessage args)
    {
        if (!this.IsPowered(ent, EntityManager) || ent.Comp.Queue.Count >= JukeboxComponent.MaxQueueLength || !_protoManager.HasIndex(args.Song))
            return;

        ent.Comp.Queue.Add(args.Song);
        ent.Comp.QueueRevision++;
        Dirty(ent);
    }

    private void OnEditQueue(Entity<JukeboxComponent> ent, ref JukeboxEditQueueMessage args)
    {
        var queue = ent.Comp.Queue;
        if (!this.IsPowered(ent, EntityManager) || args.Revision != ent.Comp.QueueRevision ||
            args.Index < 0 || args.Index >= queue.Count || args.Direction < -1 || args.Direction > 1)
            return;

        if (args.Direction == 0)
            queue.RemoveAt(args.Index);
        else
        {
            var target = args.Index + args.Direction;
            if (target < 0 || target >= queue.Count)
                return;

            (queue[args.Index], queue[target]) = (queue[target], queue[args.Index]);
        }

        ent.Comp.QueueRevision++;
        Dirty(ent);
    }

    private void OnNext(Entity<JukeboxComponent> ent, ref JukeboxNextMessage args)
    {
        if (this.IsPowered(ent, EntityManager))
            PlayNext(ent);
    }

    private bool StartTrack(Entity<JukeboxComponent> ent, ProtoId<JukeboxPrototype> songId)
    {
        if (!this.IsPowered(ent, EntityManager) || !_protoManager.TryIndex(songId, out var song))
            return false;

        var parameters = AudioParams.Default.WithVolume(SharedAudioSystem.GainToVolume(ent.Comp.VolumePercent / 100f)).WithMaxDistance(10f);
        if (Audio.PlayPvs(song.PositionalPath, ent, parameters) is not { } stream)
            return false;

        var previous = ent.Comp.AudioStream;
        ent.Comp.AudioStream = stream.Entity;
        ent.Comp.SelectedSongId = songId;
        ent.Comp.TrackDuration = Audio.GetAudioLength(Audio.ResolveSound(song.PositionalPath));
        if (ent.Comp.Broadcasting)
            _studioRadio.ReplaceMusic(ent, stream.Entity, song.PositionalPath);

        Audio.Stop(previous);
        Dirty(ent);
        return true;
    }

    private void PlayNext(Entity<JukeboxComponent> ent)
    {
        while (ent.Comp.Queue.Count > 0)
        {
            var song = ent.Comp.Queue[0];
            ent.Comp.Queue.RemoveAt(0);
            ent.Comp.QueueRevision++;
            if (StartTrack(ent, song))
                return;
        }

        Stop(ent);
    }

    private void OnJukeboxSetVolume(EntityUid uid, JukeboxComponent component, JukeboxSetVolumeMessage args)
    {
        if (!Equals(args.UiKey, JukeboxUiKey.Key) || args.VolumePercent > JukeboxComponent.MaxVolumePercent || !this.IsPowered(uid, EntityManager))
            return;

        component.PendingVolumePercent = args.VolumePercent;
        ApplyPendingVolume(uid, component);
    }

    private void ApplyPendingVolume(EntityUid uid, JukeboxComponent component)
    {
        if (component.PendingVolumePercent is not { } volume || _timing.CurTime < component.NextVolumeChange)
            return;

        component.PendingVolumePercent = null;
        if (volume == component.VolumePercent)
            return;

        component.VolumePercent = volume;
        component.NextVolumeChange = _timing.CurTime + VolumeChangeInterval;
        Audio.SetGain(component.AudioStream, volume / 100f);
        Dirty(uid, component);
    }

    private void UpdatePlayback(EntityUid uid, JukeboxComponent component)
    {
        ApplyPendingVolume(uid, component);
        if (component.AudioStream == null || !this.IsPowered(uid, EntityManager))
            return;

        if (!TryComp<AudioComponent>(component.AudioStream, out var audio) ||
            (audio.State == AudioState.Playing && _timing.CurTime - audio.AudioStart >= component.TrackDuration))
            PlayNext((uid, component));
    }
}
