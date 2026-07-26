using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;

namespace Content.Client._WH40K.CharacterCreation;

/// <summary>
///     Typography contract shared by the WH40K onboarding UI.
/// </summary>
internal static class Wh40kOnboardingTypography
{
    private const string DisplayFontPath = "/Fonts/_WH40K/CormorantSC/CormorantSC-Regular.ttf";
    private const string TextFontPath = "/Fonts/NotoSans/NotoSans-Regular.ttf";
    private const string TextBoldFontPath = "/Fonts/NotoSans/NotoSans-Bold.ttf";
    private const string TechnicalFontPath = "/EngineFonts/NotoSans/NotoSansMono-Regular.ttf";

    public const int ShellTitleSize = 25;
    public const int PanelTitleSize = 23;
    public const int SectionTitleSize = 21;
    public const int DisplaySmallSize = 17;
    public const int PreviewTitleSize = 16;
    public const int NameInputSize = 16;
    public const int NavigationTitleSize = 14;
    public const int BodySize = 13;
    public const int BodyCompactSize = 12;
    public const int BodySmallSize = 11;
    public const int StepButtonSize = 18;
    public const int TechnicalLargeSize = 14;
    public const int TechnicalValueSize = 13;
    public const int TechnicalNavigationSize = 12;
    public const int TechnicalSmallSize = 10;
    public const int TechnicalMetaSize = 9;

    public static VectorFont Display(IResourceCache resourceCache, int size)
    {
        return new VectorFont(resourceCache.GetResource<FontResource>(DisplayFontPath), size);
    }

    public static VectorFont Text(IResourceCache resourceCache, int size)
    {
        return new VectorFont(resourceCache.GetResource<FontResource>(TextFontPath), size);
    }

    public static VectorFont TextBold(IResourceCache resourceCache, int size)
    {
        return new VectorFont(resourceCache.GetResource<FontResource>(TextBoldFontPath), size);
    }

    public static VectorFont Technical(IResourceCache resourceCache, int size)
    {
        return new VectorFont(resourceCache.GetResource<FontResource>(TechnicalFontPath), size);
    }
}
