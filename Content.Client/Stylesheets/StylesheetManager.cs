using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.IoC;

namespace Content.Client.Stylesheets
{
    public sealed partial class StylesheetManager : IStylesheetManager
    {
        [Dependency] private IUserInterfaceManager _userInterfaceManager = default!;
        [Dependency] private IResourceCache _resourceCache = default!;

        public Stylesheet SheetNano { get; private set; } = default!;
        public Stylesheet SheetSpace { get; private set; } = default!;

        public void Initialize()
        {
            SheetNano = new StyleNano(_resourceCache).Stylesheet;
            // Legacy callers must use the same controls and palette as ordinary game windows.
            SheetSpace = SheetNano;

            _userInterfaceManager.Stylesheet = SheetNano;
        }
    }
}
