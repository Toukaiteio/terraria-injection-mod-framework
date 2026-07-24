using TIMF.Abstractions;

namespace TIMF.Core.Modding
{
    /// <summary>Concrete <see cref="IClientServices"/> wired by GameHooks.</summary>
    internal sealed class ClientServices : IClientServices
    {
        public IImmediateModeUi Ui
        {
            get
            {
                IImmediateModeUi ui;
                return Services != null && Services.TryGetService(out ui) ? ui : null;
            }
        }

        public IKeybindService Keybinds { get; set; }
        public IPlayerUpdateHookRegistry PlayerUpdate { get; set; }
        public IMapOverlayHookRegistry MapOverlay { get; set; }
        public IInfoAccessoryHookRegistry InfoAccessories { get; set; }

        /// <summary>Shared bag for library services such as TIMF.UI.</summary>
        public IServiceRegistry Services { get; set; }
    }
}
