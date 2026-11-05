using Content.Server.Power.EntitySystems;
using Content.Shared._WH40K.VendingMachines;
using Content.Shared.VendingMachines;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;

namespace Content.Server.VendingMachines;

public sealed partial class VendingMachineSystem
{
    [Dependency] private UserInterfaceSystem _keypadUi = default!;

    private void OnKeypadAudio(EntityUid uid, VendingMachineComponent component, VendingMachineKeypadAudioMessage args)
    {
        if (component.Broken || !this.IsPowered(uid, EntityManager) || !float.IsFinite(args.Pitch))
            return;

        var path = args.Sound switch
        {
            VendingMachineKeypadSound.Beep => "/Audio/Machines/Nuke/general_beep.ogg",
            VendingMachineKeypadSound.Success => "/Audio/Machines/vending_jingle.ogg",
            VendingMachineKeypadSound.Error => "/Audio/Machines/buzz-two.ogg",
            VendingMachineKeypadSound.Timeout => "/Audio/Machines/button.ogg",
            _ => null,
        };
        if (path == null)
            return;

        var cooldown = EnsureComp<VendingMachineKeypadAudioComponent>(uid);
        if (_timing.CurTime < cooldown.NextSound)
            return;
        cooldown.NextSound = _timing.CurTime + TimeSpan.FromSeconds(0.06);

        var volume = args.Sound == VendingMachineKeypadSound.Timeout ? -6f : -4f;
        Audio.PlayPredicted(new SoundPathSpecifier(path), uid, args.Actor,
            AudioParams.Default.WithVolume(volume).WithPitchScale(Math.Clamp(args.Pitch, 0.25f, 4f)));
    }
}

[RegisterComponent]
public sealed partial class VendingMachineKeypadAudioComponent : Component
{
    public TimeSpan NextSound;
}
