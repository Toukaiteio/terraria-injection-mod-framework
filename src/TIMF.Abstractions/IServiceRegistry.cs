namespace TIMF.Abstractions
{
    /// <summary>
    /// Cross-mod service hub. Library mods (e.g. TIMF.UI) register interfaces here;
    /// consumers resolve them via <see cref="IModContext.Services"/>.
    /// </summary>
    public interface IServiceRegistry
    {
        void Register<TService>(TService instance) where TService : class;
        TService GetService<TService>() where TService : class;
        bool TryGetService<TService>(out TService service) where TService : class;
    }
}
