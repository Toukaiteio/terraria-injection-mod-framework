using Microsoft.Xna.Framework;
using TIMF.Abstractions;

namespace TIMF.UI
{
    /// <summary>
    /// Client library mod: registers immediate-mode UI services for other mods.
    /// Id: TIMF.UI — depend with [TimfDependsOn("TIMF.UI")].
    /// Resolve via <see cref="IClientServices.Ui"/> or <see cref="IModContext.Services"/>.
    /// </summary>
    [TimfMod(Id = "TIMF.UI", Side = TimfSide.Client)]
    public sealed class TimfUiMod : IClientMod
    {
        private IModContext _ctx;
        private ImmediateModeUi _ui;

        public string Name => "TIMF.UI";
        public string Version => "1.3.0";

        public void Load(IModContext context)
        {
            _ctx = context;
            _ui = new ImmediateModeUi(context.Log);
            context.Services.Register<IImmediateModeUi>(_ui);
            context.Services.Register<IUiHost>(_ui);
            context.Log.Info("TIMF.UI library ready — IImmediateModeUi + IUiHost registered (IClientMod)");
        }

        public void Unload()
        {
            try
            {
                _ui?.DisposeResources();
            }
            catch { /* ignore */ }

            _ui = null;
            _ctx = null;
        }

        public void PostDraw(GameTime gameTime)
        {
            // Library has no own widgets; frame lifecycle is driven by Core via IUiHost.
        }
    }
}
