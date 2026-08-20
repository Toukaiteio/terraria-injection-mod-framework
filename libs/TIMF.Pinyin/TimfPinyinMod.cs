using Microsoft.Xna.Framework;
using TIMF.Abstractions;

namespace TIMF.Pinyin
{
    /// <summary>
    /// Client library mod: publishes <see cref="IPinyinService"/> for other mods to reuse.
    /// Id: TIMF.Pinyin — depend with [TimfDependsOn("TIMF.Pinyin")] and resolve via
    /// <c>context.Services.TryGetService(out IPinyinService pinyin)</c>. Mirrors the TIMF.UI
    /// library-mod pattern; ships the NPinyin dataset once for the whole install.
    /// </summary>
    [TimfMod(Id = "TIMF.Pinyin", Side = TimfSide.Client, LoadBeforeWorld = true)]
    public sealed class TimfPinyinMod : IClientMod
    {
        private IModContext _ctx;

        public string Name => "TIMF.Pinyin";
        public string Version => "1.0.0";

        public void Load(IModContext context)
        {
            _ctx = context;
            // Use the sanctioned publisher, not IServiceRegistry.Register: the security audit rejects
            // raw registration, and Publish only accepts interfaces from this mod's own assembly
            // (IPinyinService is declared in TIMF.Pinyin for exactly this reason).
            context.ServicePublisher.Publish<IPinyinService>(new PinyinService());
            context.Log.Info("TIMF.Pinyin library ready — IPinyinService published");
        }

        public void Unload()
        {
            _ctx = null;
        }

        public void PostDraw(GameTime gameTime)
        {
            // Pure logic library — no per-frame work.
        }
    }
}
