using System.Globalization;
using Content.Server._WH40K.PersistentInventory.Serialization;
using Content.Shared._WH40K.Quests;

namespace Content.Server._WH40K.Quests;

public sealed class Wh40kQuestItemStateAdapter(IEntityManager entities) : IItemStateAdapter
{
    public string ComponentId => "Wh40kQuestItem";
    public bool Handles(IComponent component) => component is Wh40kQuestItemComponent;
    public bool TryCapture(EntityUid uid, IComponent component, out PersistentInventoryComponentState state, out string? error)
    {
        var marker = (Wh40kQuestItemComponent) component;
        state = new(ComponentId, new Dictionary<string, string> { ["quest"] = marker.QuestId.ToString("D"), ["objective"] = marker.ObjectiveId });
        error = marker.QuestId == Guid.Empty || string.IsNullOrWhiteSpace(marker.ObjectiveId) || marker.ObjectiveId.Length > 64 ? "Invalid quest provenance." : null;
        return error == null;
    }
    public bool TryRestore(EntityUid uid, PersistentInventoryComponentState state, out string? error)
    {
        error = "Invalid quest provenance payload.";
        if (state.Fields.Count != 2 || !state.Fields.TryGetValue("quest", out var quest) || !Guid.TryParseExact(quest, "D", out var id) || id == Guid.Empty ||
            !state.Fields.TryGetValue("objective", out var objective) || string.IsNullOrWhiteSpace(objective) || objective.Length > 64) return false;
        var marker = entities.EnsureComponent<Wh40kQuestItemComponent>(uid);
        marker.QuestId = id; marker.ObjectiveId = objective; error = null; return true;
    }
}

public sealed class Wh40kQuestReservationStateAdapter(IEntityManager entities) : IItemStateAdapter
{
    public string ComponentId => "Wh40kQuestReservation";
    public bool Handles(IComponent component) => component is Wh40kQuestReservationComponent;
    public bool TryCapture(EntityUid uid, IComponent component, out PersistentInventoryComponentState state, out string? error)
    {
        var marker = (Wh40kQuestReservationComponent) component;
        state = new(ComponentId, new Dictionary<string, string> { ["submission"] = marker.Submission.ToString("D"),
            ["owner"] = marker.UserId.ToString("D"), ["units"] = marker.Units.ToString(CultureInfo.InvariantCulture),
            ["original"] = marker.OriginalCount.ToString(CultureInfo.InvariantCulture) });
        if (marker.Merchant) state = state with { Fields = new Dictionary<string, string>(state.Fields) { ["merchant"] = "true" } };
        error = marker.Submission == Guid.Empty || marker.UserId == Guid.Empty || marker.Units is <= 0 or > 10000 ||
            marker.OriginalCount < marker.Units ? "Invalid submission provenance." : null;
        return error == null;
    }
    public bool TryRestore(EntityUid uid, PersistentInventoryComponentState state, out string? error)
    {
        error = "Invalid submission provenance payload.";
        if (state.Fields.Count is not (4 or 5) || state.Fields.Count == 5 && (!state.Fields.TryGetValue("merchant", out var purpose) || purpose != "true") ||
            !state.Fields.TryGetValue("submission", out var text) || !Guid.TryParseExact(text, "D", out var id) || id == Guid.Empty ||
            !state.Fields.TryGetValue("owner", out var ownerText) || !Guid.TryParseExact(ownerText, "D", out var owner) || owner == Guid.Empty ||
            !state.Fields.TryGetValue("units", out var unitsText) || !int.TryParse(unitsText, NumberStyles.None, CultureInfo.InvariantCulture, out var units) || units is <= 0 or > 10000 ||
            !state.Fields.TryGetValue("original", out var originalText) || !int.TryParse(originalText, NumberStyles.None, CultureInfo.InvariantCulture, out var original) || original < units) return false;
        var marker = entities.EnsureComponent<Wh40kQuestReservationComponent>(uid);
        marker.Submission = id; marker.UserId = owner; marker.Units = units; marker.OriginalCount = original;
        marker.Merchant = state.Fields.ContainsKey("merchant"); error = null; return true;
    }
}
