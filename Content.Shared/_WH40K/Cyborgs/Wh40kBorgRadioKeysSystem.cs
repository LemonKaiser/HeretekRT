using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Lock;
using Content.Shared.Radio.Components;
using Content.Shared.Radio.EntitySystems;

namespace Content.Shared._WH40K.Cyborgs;

/// <summary>
/// Keeps a cyborg's encryption key slots gated behind its networked chassis lock.
/// </summary>
public sealed class Wh40kBorgRadioKeysSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<Wh40kBorgRadioKeysComponent, InteractUsingEvent>(OnInteractUsing, before: new[] { typeof(EncryptionKeySystem) });
        SubscribeLocalEvent<Wh40kBorgRadioKeysComponent, ExaminedEvent>(OnExamined, before: new[] { typeof(EncryptionKeySystem) });
    }

    private void OnInteractUsing(Entity<Wh40kBorgRadioKeysComponent> ent, ref InteractUsingEvent args)
    {
        SyncLock(ent);
    }

    private void OnExamined(Entity<Wh40kBorgRadioKeysComponent> ent, ref ExaminedEvent args)
    {
        SyncLock(ent);
    }

    private void SyncLock(EntityUid uid)
    {
        if (TryComp<EncryptionKeyHolderComponent>(uid, out var keys))
            keys.KeysUnlocked = !TryComp<LockComponent>(uid, out var lockComp) || !lockComp.Locked;
    }
}
