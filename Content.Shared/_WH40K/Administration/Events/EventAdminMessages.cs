using Content.Shared.Eui;
using Content.Shared.Administration;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Administration.Events;

public static class EventAdminLimits
{
    public const int Targets = 256;
    public const int Teams = 12;
    public const int Points = 16;
    public const int NameLength = 40;
    public const int MessageLength = 1000;
    public const int TimerSeconds = 7200;
}

[Serializable, NetSerializable]
public enum EventAdminAction : byte
{
    Refresh, CreateTeam, RenameTeam, DeleteTeam, AssignTeam, UnassignTeam, BalanceTeams, SetScore,
    CreatePoint, DeletePoint, Bring, GoTo, TeleportToPoint, GoToPoint, Return,
    Freeze, FreezeMute, Unfreeze, Heal, GodOn, GodOff, RestoreEffects, Sleep, Wake, Extinguish, DropHands,
    GiveItem, EquipOutfit, MessageSelection, Announce, StartTimer, PauseTimer, ResumeTimer, StopTimer, Reset,
    FriendlyFireOn, FriendlyFireOff,
}

public static class EventAdminAccess
{
    public static bool UsesSelection(EventAdminAction action) => action is not (
        EventAdminAction.Refresh or EventAdminAction.CreateTeam or EventAdminAction.RenameTeam or EventAdminAction.DeleteTeam or
        EventAdminAction.SetScore or EventAdminAction.CreatePoint or EventAdminAction.DeletePoint or EventAdminAction.GoToPoint or
        EventAdminAction.Announce or EventAdminAction.StartTimer or EventAdminAction.PauseTimer or EventAdminAction.ResumeTimer or
        EventAdminAction.StopTimer or EventAdminAction.Reset or EventAdminAction.FriendlyFireOn or EventAdminAction.FriendlyFireOff);

    public static AdminFlags Required(EventAdminAction action) => AdminFlags.Fun | (action switch
    {
        EventAdminAction.GiveItem => AdminFlags.Spawn,
        EventAdminAction.EquipOutfit or EventAdminAction.Bring or EventAdminAction.GoTo or
            EventAdminAction.TeleportToPoint or EventAdminAction.GoToPoint or EventAdminAction.Return or
            EventAdminAction.Announce => AdminFlags.Admin,
        _ => AdminFlags.None,
    });
}

[Serializable, NetSerializable]
public sealed record EventAdminPlayer(Guid Id, string Account, string Character, int Team, bool Online,
    bool HasBody, bool Dead, bool Frozen, bool God, bool Sleeping, bool CanReturn, bool Ghost = false);

[Serializable, NetSerializable]
public sealed record EventAdminTeam(int Id, string Name, int Score, int Members, bool FriendlyFire = true);

[Serializable, NetSerializable]
public sealed record EventAdminPoint(int Id, string Name, bool Valid);

[Serializable, NetSerializable]
public sealed record EventAdminResult(string Key = "", int Applied = 0, int Skipped = 0);

[Serializable, NetSerializable]
public sealed class EventAdminEuiState : EuiStateBase
{
    public int Epoch { get; init; }
    public bool Busy { get; init; }
    public int CompletedRequest { get; init; }
    public EventAdminPlayer[] Players { get; init; } = [];
    public EventAdminAction[] AllowedActions { get; init; } = [];
    public EventAdminTeam[] Teams { get; init; } = [];
    public EventAdminPoint[] Points { get; init; } = [];
    public int SecondsLeft { get; init; }
    public bool TimerRunning { get; init; }
    public EventAdminResult Result { get; init; } = new();
}

[Serializable, NetSerializable]
public sealed class EventAdminRequest(EventAdminAction action, int epoch, Guid[]? players = null,
    int team = -1, int point = -1, string text = "", int number = 0, int requestId = 0) : EuiMessageBase
{
    public EventAdminAction Action { get; } = action;
    public int Epoch { get; } = epoch;
    public Guid[] Players { get; } = players ?? [];
    public int Team { get; } = team;
    public int Point { get; } = point;
    public string Text { get; } = text;
    public int Number { get; } = number;
    public int RequestId { get; } = requestId;
}
