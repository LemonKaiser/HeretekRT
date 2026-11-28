using Content.Shared._WH40K.ShipPa;
using Robust.Shared.Audio;

namespace Content.Server._WH40K.ShipPa;

public sealed partial class Wh40kShipPaSystem
{
    private readonly List<EntityUid> _emptyBroadcastGrids = new();

    public void StartAlarm(
        EntityUid grid,
        string key,
        SoundSpecifier sound,
        AudioParams? audioParams = null,
        string? message = null,
        Color? color = null,
        int priority = Wh40kShipPaPlaybackPolicy.DefaultAlarmPriority)
    {
        StartBroadcast(grid, key, sound, true, Wh40kShipPaBroadcastKind.Alarm, priority, audioParams, message, color);
    }

    private Wh40kShipPaBroadcast? StartBroadcast(
        EntityUid grid,
        string key,
        SoundSpecifier sound,
        bool loop,
        Wh40kShipPaBroadcastKind kind,
        int priority,
        AudioParams? audioParams = null,
        string? caption = null,
        Color? color = null)
    {
        if (!Exists(grid) || TerminatingOrDeleted(grid))
            return null;

        var resolved = _audio.ResolveSound(sound);
        var path = _audio.GetAudioPath(resolved);
        if (string.IsNullOrEmpty(path))
            return null;

        float length;
        try
        {
            length = (float) _audio.GetAudioLength(resolved).TotalSeconds;
        }
        catch (Exception e)
        {
            Log.Warning($"Couldn't load Ship Vox-PA audio {path}: {e.Message}");
            return null;
        }

        if (!float.IsFinite(length) || length <= 0f)
            return null;

        var state = EnsureComp<Wh40kShipPaBroadcastComponent>(grid);
        state.Broadcasts.RemoveAll(b => b.Key == key);
        var start = _timing.CurTime + TimeSpan.FromSeconds(Wh40kShipPaPlaybackPolicy.StartLeadSeconds);
        var broadcast = new Wh40kShipPaBroadcast
        {
            Id = NextBroadcastId(),
            Key = key,
            Path = path,
            Start = start,
            Length = length,
            Loop = loop,
            Kind = kind,
            Priority = priority,
            Caption = caption,
            Color = color ?? Color.White,
            Params = (audioParams ?? sound.Params).WithLoop(loop).WithPitchScale(1f).WithVariation(null),
            RetainUntil = start + TimeSpan.FromSeconds(kind == Wh40kShipPaBroadcastKind.Announcement && caption != null ? Math.Max(8f, length) : length),
        };
        state.Broadcasts.Add(broadcast);
        Dirty(grid, state);
        RefreshCoverage(grid);
        UpdateBroadcastLights(grid, state);
        return broadcast;
    }

    public void StopAlarm(EntityUid grid, string key)
    {
        if (!TryComp(grid, out Wh40kShipPaBroadcastComponent? state) || state.Broadcasts.RemoveAll(b => b.Key == key) == 0)
            return;

        Dirty(grid, state);
        UpdateBroadcastLights(grid, state);
        if (state.Broadcasts.Count == 0)
            RemComp<Wh40kShipPaBroadcastComponent>(grid);
    }

    public bool IsAlarmActive(EntityUid grid, string key)
    {
        return TryComp(grid, out Wh40kShipPaBroadcastComponent? state) &&
               state.Broadcasts.Exists(b => b.Key == key && b.IsActive(_timing.CurTime));
    }

    private void UpdateBroadcasts()
    {
        _emptyBroadcastGrids.Clear();
        var query = EntityQueryEnumerator<Wh40kShipPaBroadcastComponent>();
        while (query.MoveNext(out var grid, out var state))
        {
            var changed = false;
            for (var i = state.Broadcasts.Count - 1; i >= 0; i--)
            {
                var broadcast = state.Broadcasts[i];
                if (broadcast.Loop || _timing.CurTime < broadcast.RetainUntil)
                    continue;

                state.Broadcasts.RemoveAt(i);
                changed = true;
            }

            if (changed)
            {
                Dirty(grid, state);
                UpdateBroadcastLights(grid, state);
            }

            if (state.Broadcasts.Count == 0)
                _emptyBroadcastGrids.Add(grid);
        }

        foreach (var grid in _emptyBroadcastGrids)
            RemComp<Wh40kShipPaBroadcastComponent>(grid);
        _emptyBroadcastGrids.Clear();
    }

    private void UpdateBroadcastLights(EntityUid grid, Wh40kShipPaBroadcastComponent state)
    {
        TimeSpan? until = null;
        foreach (var broadcast in state.Broadcasts)
        {
            if (!broadcast.IsActive(_timing.CurTime))
                continue;

            var end = broadcast.Loop ? TimeSpan.MaxValue : broadcast.Start + TimeSpan.FromSeconds(broadcast.Length);
            if (until == null || end > until)
                until = end;
        }

        _speakerBuffer.Clear();
        GatherSpeakers(grid, _speakerBuffer);
        foreach (var speaker in _speakerBuffer)
        {
            if (speaker.Comp.BroadcastingUntil == until)
                continue;

            speaker.Comp.BroadcastingUntil = until;
            UpdateAppearance(speaker);
        }

        _speakerBuffer.Clear();
    }
}
