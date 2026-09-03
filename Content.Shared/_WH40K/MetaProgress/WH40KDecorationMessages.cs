using System;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.MetaProgress;

[Serializable, NetSerializable]
public sealed class WH40KDecorationEntry
{
    public string Id { get; }
    public WH40KMetaDecorationCategory Category { get; }
    public string TitleKey { get; }
    public string PreviewKey { get; }
    public string OocColorHex { get; }
    public string[] OocGradientColors { get; }
    public bool OocGradientAnimated { get; }
    public int OocGradientDurationMs { get; }
    public string OocAuraHex { get; }
    public int OocAuraRadius { get; }
    public int OocAuraAlphaPercent { get; }
    public string OocTitleEffect { get; }
    public int OocTitleEffectRevealMs { get; }
    public int OocTitleEffectHoldMs { get; }
    public int OocTitleEffectDissolveMs { get; }
    public string OocTitleOutlineHex { get; }
    public int OocTitleOutlineWidth { get; }
    public int OocTitleOutlineAlphaPercent { get; }
    public string GhostRsiPath { get; }
    public string GhostState { get; }
    public string GhostTintHex { get; }
    public int SortOrder { get; }
    public bool SuppressTitlePrefix { get; }

    public WH40KDecorationEntry(
        string id,
        WH40KMetaDecorationCategory category,
        string titleKey,
        string previewKey,
        string oocColorHex,
        string[] oocGradientColors,
        bool oocGradientAnimated,
        int oocGradientDurationMs,
        string oocAuraHex,
        int oocAuraRadius,
        int oocAuraAlphaPercent,
        string oocTitleEffect,
        int oocTitleEffectRevealMs,
        int oocTitleEffectHoldMs,
        int oocTitleEffectDissolveMs,
        string oocTitleOutlineHex,
        int oocTitleOutlineWidth,
        int oocTitleOutlineAlphaPercent,
        string ghostRsiPath,
        string ghostState,
        string ghostTintHex,
        int sortOrder,
        bool suppressTitlePrefix)
    {
        Id = id;
        Category = category;
        TitleKey = titleKey;
        PreviewKey = previewKey;
        OocColorHex = oocColorHex;
        OocGradientColors = oocGradientColors;
        OocGradientAnimated = oocGradientAnimated;
        OocGradientDurationMs = oocGradientDurationMs;
        OocAuraHex = oocAuraHex;
        OocAuraRadius = oocAuraRadius;
        OocAuraAlphaPercent = oocAuraAlphaPercent;
        OocTitleEffect = oocTitleEffect;
        OocTitleEffectRevealMs = oocTitleEffectRevealMs;
        OocTitleEffectHoldMs = oocTitleEffectHoldMs;
        OocTitleEffectDissolveMs = oocTitleEffectDissolveMs;
        OocTitleOutlineHex = oocTitleOutlineHex;
        OocTitleOutlineWidth = oocTitleOutlineWidth;
        OocTitleOutlineAlphaPercent = oocTitleOutlineAlphaPercent;
        GhostRsiPath = ghostRsiPath;
        GhostState = ghostState;
        GhostTintHex = ghostTintHex;
        SortOrder = sortOrder;
        SuppressTitlePrefix = suppressTitlePrefix;
    }
}

[Serializable, NetSerializable]
public sealed class WH40KDecorationSelection : IEquatable<WH40KDecorationSelection>
{
    public static readonly WH40KDecorationSelection Empty = new(string.Empty, string.Empty, string.Empty);

    public string SelectedGhostSkinId { get; }
    public string SelectedOocTitleId { get; }
    public string SelectedOocNameColorId { get; }

    public WH40KDecorationSelection(string selectedGhostSkinId, string selectedOocTitleId, string selectedOocNameColorId)
    {
        SelectedGhostSkinId = selectedGhostSkinId;
        SelectedOocTitleId = selectedOocTitleId;
        SelectedOocNameColorId = selectedOocNameColorId;
    }

    public bool Equals(WH40KDecorationSelection? other)
    {
        return other != null &&
               SelectedGhostSkinId == other.SelectedGhostSkinId &&
               SelectedOocTitleId == other.SelectedOocTitleId &&
               SelectedOocNameColorId == other.SelectedOocNameColorId;
    }

    public override bool Equals(object? obj)
        => obj is WH40KDecorationSelection other && Equals(other);

    public override int GetHashCode()
        => HashCode.Combine(SelectedGhostSkinId, SelectedOocTitleId, SelectedOocNameColorId);

    public WH40KDecorationSelection WithSelection(WH40KMetaDecorationCategory category, string id)
    {
        return category switch
        {
            WH40KMetaDecorationCategory.GhostSkins => new WH40KDecorationSelection(
                id,
                SelectedOocTitleId,
                SelectedOocNameColorId),
            WH40KMetaDecorationCategory.OocTitles => new WH40KDecorationSelection(
                SelectedGhostSkinId,
                id,
                SelectedOocNameColorId),
            WH40KMetaDecorationCategory.OocNameColors => new WH40KDecorationSelection(
                SelectedGhostSkinId,
                SelectedOocTitleId,
                id),
            _ => this,
        };
    }
}

[Serializable, NetSerializable]
public sealed class WH40KDecorationState
{
    public long ServerEpoch { get; }
    public long Revision { get; }
    public long CatalogRevision { get; }
    /// <summary>
    ///     Identifier of the selection request this state answers, or zero for an unsolicited refresh.
    /// </summary>
    public long AcknowledgedSelectionRequestId { get; }
    public WH40KDecorationEntry[] Decorations { get; }
    public WH40KDecorationSelection Selection { get; }

    public WH40KDecorationState(
        long serverEpoch,
        long revision,
        long catalogRevision,
        long acknowledgedSelectionRequestId,
        WH40KDecorationEntry[] decorations,
        WH40KDecorationSelection selection)
    {
        ServerEpoch = serverEpoch;
        Revision = revision;
        CatalogRevision = catalogRevision;
        AcknowledgedSelectionRequestId = acknowledgedSelectionRequestId;
        Decorations = decorations;
        Selection = selection;
    }
}

[Serializable, NetSerializable]
public sealed class WH40KDecorationRequestStateEvent : EntityEventArgs
{
}

[Serializable, NetSerializable]
public sealed class WH40KDecorationStateEvent : EntityEventArgs
{
    public WH40KDecorationState State { get; }

    public WH40KDecorationStateEvent(WH40KDecorationState state)
    {
        State = state;
    }
}

[Serializable, NetSerializable]
public sealed class WH40KDecorationSetSelectionEvent : EntityEventArgs
{
    public WH40KMetaDecorationCategory Category { get; }
    public string DecorationId { get; }
    public long RequestId { get; }

    public WH40KDecorationSetSelectionEvent(WH40KMetaDecorationCategory category, string decorationId, long requestId)
    {
        Category = category;
        DecorationId = decorationId;
        RequestId = requestId;
    }
}
