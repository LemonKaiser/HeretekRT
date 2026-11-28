using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Synthetics;

public sealed partial class Wh40kOpenChassisPanelActionEvent : InstantActionEvent;

public sealed partial class Wh40kToggleOverclockActionEvent : InstantActionEvent;

[Serializable, NetSerializable]
public sealed partial class Wh40kChassisPanelLinkDoAfterEvent : SimpleDoAfterEvent;

[Serializable, NetSerializable]
public sealed partial class Wh40kChassisBayDoAfterEvent : SimpleDoAfterEvent
{
    public readonly bool Open;

    public Wh40kChassisBayDoAfterEvent(bool open)
    {
        Open = open;
    }
}

[Serializable, NetSerializable]
public sealed partial class Wh40kChassisPullOrganDoAfterEvent : SimpleDoAfterEvent
{
    public readonly NetEntity Organ;

    public Wh40kChassisPullOrganDoAfterEvent(NetEntity organ)
    {
        Organ = organ;
    }
}

[Serializable, NetSerializable]
public sealed partial class Wh40kChassisInstallOrganDoAfterEvent : SimpleDoAfterEvent
{
    public readonly NetEntity Organ;
    public readonly NetEntity Part;
    public readonly string Slot;

    public Wh40kChassisInstallOrganDoAfterEvent(NetEntity organ, NetEntity part, string slot)
    {
        Organ = organ;
        Part = part;
        Slot = slot;
    }
}

[Serializable, NetSerializable]
public sealed partial class Wh40kLimbReattachDoAfterEvent : SimpleDoAfterEvent
{
    public readonly NetEntity Limb;
    public readonly NetEntity Parent;
    public readonly string Slot;

    public Wh40kLimbReattachDoAfterEvent(NetEntity limb, NetEntity parent, string slot)
    {
        Limb = limb;
        Parent = parent;
        Slot = slot;
    }
}

[Serializable, NetSerializable]
public sealed partial class Wh40kLooseCouplingRepairDoAfterEvent : SimpleDoAfterEvent
{
    public readonly NetEntity Part;
    public readonly bool Weld;

    public Wh40kLooseCouplingRepairDoAfterEvent(NetEntity part, bool weld)
    {
        Part = part;
        Weld = weld;
    }
}

/// <summary>Raised on a chassis body when a reboot sequence completes.</summary>
[ByRefEvent]
public readonly record struct Wh40kChassisRebootedEvent;
