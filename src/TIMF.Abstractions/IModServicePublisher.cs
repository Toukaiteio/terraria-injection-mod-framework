namespace TIMF.Abstractions
{
    /// <summary>Publishes mod-owned service interfaces without replacing framework services.</summary>
    public interface IModServicePublisher
    {
        void Publish<TService>(TService instance) where TService : class;
    }
}
