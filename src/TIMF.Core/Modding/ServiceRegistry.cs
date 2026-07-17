using System;
using System.Collections.Generic;
using TIMF.Abstractions;

namespace TIMF.Core.Modding
{
    internal sealed class ServiceRegistry : IServiceRegistry
    {
        private readonly Dictionary<Type, object> _map = new Dictionary<Type, object>();
        private readonly object _lock = new object();

        public void Register<TService>(TService instance) where TService : class
        {
            if (instance == null)
                throw new ArgumentNullException(nameof(instance));
            lock (_lock)
            {
                _map[typeof(TService)] = instance;
            }
        }

        public TService GetService<TService>() where TService : class
        {
            TService s;
            if (!TryGetService(out s) || s == null)
                throw new InvalidOperationException("Service not registered: " + typeof(TService).FullName);
            return s;
        }

        public bool TryGetService<TService>(out TService service) where TService : class
        {
            lock (_lock)
            {
                object o;
                if (_map.TryGetValue(typeof(TService), out o) && o is TService)
                {
                    service = (TService)o;
                    return true;
                }
            }

            service = null;
            return false;
        }
    }
}
