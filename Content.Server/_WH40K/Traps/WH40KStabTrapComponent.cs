using Content.Shared.Damage;
using Robust.Shared.Audio;

namespace Content.Server._WH40K.Traps;

/// <summary>
/// Configuration and transient activation state for a retractable floor spike trap.
/// </summary>
[RegisterComponent, Access(typeof(WH40KStabTrapSystem))]
public sealed partial class WH40KStabTrapComponent : Component
{
    /// <summary>
    /// Damage dealt to every damageable entity standing on the trap when its spikes fully extend.
    /// </summary>
    [DataField("damage", required: true)]
    public DamageSpecifier Damage = default!;

    /// <summary>
    /// Time from stepping on the trap to the damaging fully-extended frame.
    /// The first partially extended frame is shown immediately; the rest of the
    /// opening animation reaches this frame after the one-second pause.
    /// </summary>
    [DataField]
    public float ActivationDelay = 1.09f;

    /// <summary>
    /// How long the fully-extended animation frame remains visible before the spikes retract.
    /// </summary>
    [DataField]
    public float ExtendedDuration = 2f;

    /// <summary>
    /// Duration of the fast retraction at the end of the activation animation.
    /// </summary>
    [DataField]
    public float RetractionDuration = 0.15f;

    /// <summary>
    /// Time after retraction before the trap can be triggered again.
    /// </summary>
    [DataField]
    public float RechargeDelay = 5f;

    /// <summary>
    /// Sound played when the spike mechanism begins to extend.
    /// </summary>
    [DataField]
    public SoundSpecifier ExtendSound = new SoundPathSpecifier("/Audio/_WH40K/Traps/spike_trap_activate.ogg");

    /// <summary>
    /// Sound played when the extended spikes begin to retract.
    /// </summary>
    [DataField]
    public SoundSpecifier RetractSound = new SoundPathSpecifier("/Audio/_WH40K/Traps/spike_trap_activate.ogg");

    /// <summary>
    /// Sound played when the trap successfully inflicts piercing damage.
    /// </summary>
    [DataField]
    public SoundSpecifier StrikeSound = new SoundPathSpecifier("/Audio/Weapons/pierce.ogg");

    [ViewVariables]
    public WH40KStabTrapPhase Phase;

    [ViewVariables]
    public float PhaseTimeRemaining;

    /// <summary>
    /// Whether the trap tile contained a living mob or actively thrown item during the previous update.
    /// The trap can only be triggered by a transition from an empty tile to an occupied one.
    /// </summary>
    public bool TileWasOccupied;

    /// <summary>
    /// Prevents a second damage application if an unexpected phase transition occurs.
    /// It is reset exclusively when a new activation cycle starts.
    /// </summary>
    public bool StrikeResolvedThisCycle;

    /// <summary>
    /// Reused buffer for the local-tile query.
    /// </summary>
    public HashSet<EntityUid> QueriedOccupants = new();
}

/// <summary>
/// The server-side activation cycle. The visual state is synchronized separately through Appearance.
/// </summary>
public enum WH40KStabTrapPhase : byte
{
    Ready,
    Activating,
    Extended,
    Retracting,
    Recharging,
}
