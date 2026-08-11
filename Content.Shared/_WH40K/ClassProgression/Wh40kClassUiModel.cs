namespace Content.Shared._WH40K.ClassProgression;

/// <summary>
/// Small deterministic client-side state machine. A purchase only sets Pending; the displayed snapshot can only be
/// replaced by <see cref="ApplyServerResponse"/>.
/// </summary>
public sealed class Wh40kClassUiModel
{
    public Wh40kClassUiSnapshot? Snapshot { get; private set; }
    public Wh40kClassUiOperationStatus Status { get; private set; }
    public string? SelectedSkillId { get; private set; }
    public bool PurchasePending { get; private set; }

    public bool SelectSkill(string skillId)
    {
        if (!ContainsSkill(Snapshot, skillId))
            return false;

        SelectedSkillId = skillId;
        return true;
    }

    public bool BeginPurchase(string skillId)
    {
        if (PurchasePending || Snapshot == null || GetNodeState(Snapshot, skillId) != Wh40kClassSkillNodeState.Available)
            return false;

        SelectedSkillId = skillId;
        PurchasePending = true;
        Status = Wh40kClassUiOperationStatus.None;
        return true;
    }

    public void ApplyServerResponse(
        Wh40kClassUiOperationStatus status,
        Wh40kClassUiSnapshot? snapshot)
    {
        Status = status;
        PurchasePending = false;

        if (snapshot != null)
            Snapshot = snapshot;
        else if (status == Wh40kClassUiOperationStatus.AccountUnavailable)
            Snapshot = null;

        if (SelectedSkillId != null && !ContainsSkill(Snapshot, SelectedSkillId))
            SelectedSkillId = null;
    }

    public static Wh40kClassSkillNodeState? GetNodeState(
        Wh40kClassUiSnapshot? snapshot,
        string skillId)
    {
        if (snapshot == null)
            return null;

        foreach (var specialization in snapshot.Tree.Specializations)
        {
            foreach (var skill in specialization.Skills)
            {
                if (skill.SkillId == skillId)
                    return skill.State;
            }
        }

        return null;
    }

    private static bool ContainsSkill(Wh40kClassUiSnapshot? snapshot, string skillId)
    {
        return GetNodeState(snapshot, skillId) != null;
    }
}
