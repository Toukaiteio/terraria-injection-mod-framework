using System;
using System.Collections.Generic;
using System.Reflection;
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

        internal void RegisterModService(Type serviceType, object instance, Assembly ownerAssembly, string modId)
        {
            if (serviceType == null || instance == null || ownerAssembly == null)
                throw new ArgumentNullException();
            if (!serviceType.IsInterface || serviceType.Assembly != ownerAssembly)
                throw new UnauthorizedAccessException("Mods may publish only interfaces declared by their own assembly.");
            if (!serviceType.IsInstanceOfType(instance))
                throw new ArgumentException("Published instance does not implement " + serviceType.FullName);
            lock (_lock)
            {
                if (_map.ContainsKey(serviceType))
                    throw new InvalidOperationException("Service is already registered and cannot be replaced: " + serviceType.FullName);
                _map.Add(serviceType, instance);
            }
        }
    }

    internal sealed class ModServicePublisher : IModServicePublisher
    {
        private readonly ServiceRegistry _registry;
        private readonly Assembly _assembly;
        private readonly string _modId;
        public ModServicePublisher(ServiceRegistry registry, Assembly assembly, string modId)
        { _registry = registry; _assembly = assembly; _modId = modId; }
        public void Publish<TService>(TService instance) where TService : class =>
            _registry.RegisterModService(typeof(TService), instance, _assembly, _modId);
    }
}
