using System.Numerics;
using Content.Shared.Salvage;
using Content.Shared.Salvage.Expeditions;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype.List;

namespace Content.Server.Salvage.Expeditions;

/// <summary>
/// Designates this entity as holding a salvage expedition.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class SalvageExpeditionComponent : SharedSalvageExpeditionComponent
{
    public SalvageMissionParams MissionParams = default!;

    /// <summary>
    /// Where the dungeon is located for initial announcement.
    /// </summary>
    [DataField("dungeonLocation")]
    public Vector2 DungeonLocation = Vector2.Zero;

    /// <summary>
    /// When the expeditions ends.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField("endTime", customTypeSerializer: typeof(TimeOffsetSerializer))]
    [AutoPausedField]
    public TimeSpan EndTime;

    /// <summary>
    /// Expedition owner whose mission this is. Legacy station owners and independent vessel-grid
    /// owners both use this field.
    /// </summary>
    [DataField("station")]
    public EntityUid Station;

    /// <summary>
    /// Map from which the owner entered the expedition. Used outside Koronus sector maps.
    /// </summary>
    [DataField]
    public EntityUid? ReturnMap;

    /// <summary>
    /// Koronus system from which the owner entered the expedition. The system id survives a
    /// cold-unload of its runtime map and is therefore preferred over <see cref="ReturnMap"/>.
    /// </summary>
    [DataField]
    public string? ReturnSystemId;

    [ViewVariables] public bool Completed = false;

    /// <summary>
    /// Countdown audio stream.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityUid? Stream = null;

    /// <summary>
    /// Sound that plays when the mission end is imminent.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField]
    public SoundSpecifier Sound = new SoundCollectionSpecifier("ExpeditionEnd")
    {
        Params = AudioParams.Default.WithVolume(-5),
    };

    /// <summary>
    /// Song selected on MapInit so we can predict the audio countdown properly.
    /// </summary>
    [DataField]
    public ResolvedSoundSpecifier SelectedSong;

    // Frontier: expedition difficulty and rewards
    /// <summary>
    /// The difficulty this mission had or, in the future, was selected.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField("difficulty")]
    public DifficultyRating Difficulty;

    /// <summary>
    /// List of items to order on mission completion
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField("rewards", customTypeSerializer: typeof(PrototypeIdListSerializer<EntityPrototype>))]
    public List<string> Rewards = default!;
    // End Frontier: expedition difficulty and rewards
}
