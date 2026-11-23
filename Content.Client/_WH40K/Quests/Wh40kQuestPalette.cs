using Content.Shared._WH40K.Quests;
using Robust.Client.Graphics;
using Robust.Shared.Maths;

namespace Content.Client._WH40K.Quests;

internal static class Wh40kQuestPalette
{
    public static readonly Color Card = Color.FromHex("#18140F");
    public static readonly Color CardBorder = Color.FromHex("#56452D");
    public static readonly Color Separator = Color.FromHex("#56452D");
    public static readonly Color TitleText = Color.FromHex("#E9E1D0");
    public static readonly Color BodyText = Color.FromHex("#CFC4B0");
    public static readonly Color MutedText = Color.FromHex("#ADA391");
    public static readonly Color HeaderText = Color.FromHex("#ADA391");
    public static readonly Color RewardText = Color.FromHex("#ADA391");
    public static readonly Color StateOffer = Color.FromHex("#6E9ECF");
    public static readonly Color StateActive = Color.FromHex("#ADA391");
    public static readonly Color StateReady = Color.FromHex("#28B03D");
    public static readonly Color StateCompleted = Color.FromHex("#31843E");
    public static readonly Color StateFailed = Color.FromHex("#BB3232");
    public static readonly Color StateAbandoned = Color.FromHex("#5A5A5A");
    public static readonly Color StateExpired = Color.FromHex("#5A5A5A");
    public static readonly Color ProgressFill = Color.FromHex("#6E9ECF");
    public static readonly Color ProgressComplete = Color.FromHex("#28B03D");
    public static readonly Color ProgressBg = Color.FromHex("#100D0A");
    public static readonly Color ResultOk = Color.FromHex("#31843E");
    public static readonly Color ResultPending = Color.FromHex("#A5762F");
    public static readonly Color ResultError = Color.FromHex("#BB3232");
    public static readonly Color WarningBg = Color.FromHex("#2D2520");
    public static readonly Color WarningBorder = Color.FromHex("#A5762F");

    public static Color GetStateColor(Wh40kQuestState state) => state switch
    {
        Wh40kQuestState.Active => StateActive,
        Wh40kQuestState.ReadyToTurnIn => StateReady,
        Wh40kQuestState.Completed => StateCompleted,
        Wh40kQuestState.Failed => StateFailed,
        Wh40kQuestState.Abandoned => StateAbandoned,
        Wh40kQuestState.Expired => StateExpired,
        _ => MutedText,
    };

    public static Color GetResultColor(Wh40kQuestOperationResult result) => result switch
    {
        Wh40kQuestOperationResult.Succeeded => ResultOk,
        Wh40kQuestOperationResult.Pending => ResultPending,
        _ => ResultError,
    };

    public static StyleBoxFlat CreateCardPanel() => new()
    {
        BackgroundColor = Card,
        BorderColor = CardBorder,
        BorderThickness = new Thickness(0, 1, 1, 1),
    };

    public static StyleBoxFlat CreateWarningPanel() => new()
    {
        BackgroundColor = WarningBg,
        BorderColor = WarningBorder.WithAlpha(0.45f),
        BorderThickness = new Thickness(1),
        ContentMarginLeftOverride = 6f,
        ContentMarginRightOverride = 6f,
        ContentMarginTopOverride = 3f,
        ContentMarginBottomOverride = 3f,
    };

    public static StyleBoxFlat CreateProgressBg() => new()
    {
        BackgroundColor = ProgressBg,
        ContentMarginLeftOverride = 0f,
        ContentMarginRightOverride = 0f,
        ContentMarginTopOverride = 0f,
        ContentMarginBottomOverride = 0f,
    };

    public static StyleBoxFlat CreateProgressFill(bool complete) => new()
    {
        BackgroundColor = complete ? ProgressComplete : ProgressFill,
        ContentMarginLeftOverride = 0f,
        ContentMarginRightOverride = 0f,
        ContentMarginTopOverride = 0f,
        ContentMarginBottomOverride = 0f,
    };
}
