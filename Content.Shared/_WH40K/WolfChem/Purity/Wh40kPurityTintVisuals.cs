using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.WolfChem.Purity;

/// <summary>Appearance key, true while a <c>Wh40kPurityTint</c> entity's reagent is at or above its grade.</summary>
[Serializable, NetSerializable]
public enum Wh40kPurityTintVisuals : byte
{
    Tinted,
}
