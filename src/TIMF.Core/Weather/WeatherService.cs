using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Terraria;
using TIMF.Abstractions;

namespace TIMF.Core.Weather
{
    /// <summary>
    /// Framework weather registry: vanilla channels + plugin-registered channels,
    /// optional lock re-applied after <c>Main.UpdateWeather</c>.
    /// </summary>
    internal sealed class WeatherService : IWeatherService
    {
        private readonly ILogger _log;
        private readonly Dictionary<string, IWeatherChannel> _channels =
            new Dictionary<string, IWeatherChannel>(StringComparer.OrdinalIgnoreCase);
        private readonly object _lock = new object();
        private readonly List<IWeatherChannel> _ordered = new List<IWeatherChannel>();

        private Harmony _harmony;
        private WeatherBundle _lockedBundle;
        private bool _lockEnabled;
        private bool _applying;
        private int _syncCooldown;

        public WeatherService(ILogger log)
        {
            _log = log;
            foreach (var ch in VanillaWeatherChannels.CreateAll())
                RegisterInternal(ch);
            RebuildOrdered();
            _log.Info("WeatherService: registered " + _channels.Count + " vanilla weather channels");
        }

        public IReadOnlyList<IWeatherChannel> Channels
        {
            get { lock (_lock) return _ordered.ToArray(); }
        }

        public bool IsLockEnabled
        {
            get { lock (_lock) return _lockEnabled; }
        }

        public WeatherBundle LockedBundle
        {
            get { lock (_lock) return _lockedBundle; }
        }

        public void Register(IWeatherChannel channel)
        {
            if (channel == null || string.IsNullOrWhiteSpace(channel.Id))
                throw new ArgumentException("Weather channel requires a non-empty Id.");

            lock (_lock)
            {
                RegisterInternal(channel);
                RebuildOrdered();
            }
            _log.Info("WeatherService: registered channel " + channel.Id + " (" + channel.DisplayName + ")");
        }

        public bool Unregister(string id)
        {
            if (string.IsNullOrEmpty(id))
                return false;
            lock (_lock)
            {
                if (!_channels.Remove(id))
                    return false;
                RebuildOrdered();
            }
            _log.Info("WeatherService: unregistered channel " + id);
            return true;
        }

        public bool TryGet(string id, out IWeatherChannel channel)
        {
            lock (_lock)
                return _channels.TryGetValue(id ?? "", out channel);
        }

        public IReadOnlyList<IWeatherChannel> GetByCategory(WeatherCategory category)
        {
            lock (_lock)
            {
                return _ordered.Where(c => c.Category == category).ToArray();
            }
        }

        public WeatherSnapshot Capture()
        {
            var snap = new WeatherSnapshot();
            try
            {
                snap.WindSpeed = Main.windSpeedTarget;
                snap.MoonPhase = Main.moonPhase;
                snap.Raining = Main.raining;
                snap.RainIntensity = Main.maxRaining;
                snap.Sandstorm = SandstormHappening();
                snap.SlimeRain = Main.slimeRain;
                snap.BloodMoon = Main.bloodMoon;
                snap.PumpkinMoon = Main.pumpkinMoon;
                snap.FrostMoon = Main.snowMoon;
                snap.LanternNight = LanternUp();
                snap.CloudCount = Main.numClouds;
            }
            catch { /* ignore */ }

            lock (_lock)
            {
                foreach (var kv in _channels)
                {
                    try { snap.Channels[kv.Key] = kv.Value.Read(); }
                    catch { /* skip broken channel */ }
                }
            }

            snap.Summary = BuildSummary(snap);
            return snap;
        }

        public bool TrySet(string channelId, WeatherValue value, WeatherSetOptions options, out string error)
        {
            error = null;
            if (!IsAuthority())
            {
                error = "Weather writes require world authority (SP / host / dedicated).";
                return false;
            }

            IWeatherChannel ch;
            if (!TryGet(channelId, out ch) || ch == null)
            {
                error = "Unknown weather channel: " + channelId;
                return false;
            }

            if (!ch.CanWrite)
            {
                error = "Channel is read-only: " + channelId;
                return false;
            }

            options = options ?? new WeatherSetOptions();
            if (!ch.TryWrite(value, options, out error))
                return false;

            if (options.SyncNetwork)
                SyncToClients();
            return true;
        }

        public bool TryApplyBundle(WeatherBundle bundle, out string error)
        {
            error = null;
            if (bundle == null)
            {
                error = "Bundle is null.";
                return false;
            }
            if (!IsAuthority())
            {
                error = "Weather writes require world authority (SP / host / dedicated).";
                return false;
            }
            if (_applying)
            {
                error = "Re-entrant weather apply.";
                return false;
            }

            _applying = true;
            try
            {
                var opt = new WeatherSetOptions
                {
                    Instant = bundle.Instant,
                    SyncNetwork = false,
                };

                if (!string.IsNullOrEmpty(bundle.AtmospherePreset))
                {
                    var v = WeatherValue.FromString(bundle.AtmospherePreset);
                    if (bundle.RainIntensity.HasValue)
                        v.FloatValue = bundle.RainIntensity;
                    string e;
                    if (!TrySetNoAuthCheck(WeatherChannelIds.AtmospherePreset, v, opt, out e))
                    {
                        error = e;
                        return false;
                    }
                }

                if (bundle.WindSpeed.HasValue)
                {
                    string e;
                    if (!TrySetNoAuthCheck(WeatherChannelIds.WindSpeed, WeatherValue.FromFloat(bundle.WindSpeed.Value), opt, out e))
                    {
                        error = e;
                        return false;
                    }
                }

                if (bundle.MoonPhase.HasValue)
                {
                    string e;
                    if (!TrySetNoAuthCheck(WeatherChannelIds.MoonPhase, WeatherValue.FromInt(bundle.MoonPhase.Value), opt, out e))
                    {
                        error = e;
                        return false;
                    }
                }

                if (bundle.DisableEvents != null)
                {
                    foreach (var id in bundle.DisableEvents)
                    {
                        string e;
                        TrySetNoAuthCheck(id, WeatherValue.FromBool(false), opt, out e);
                    }
                }

                if (bundle.EnableEvents != null)
                {
                    foreach (var id in bundle.EnableEvents)
                    {
                        string e;
                        TrySetNoAuthCheck(id, WeatherValue.FromBool(true), opt, out e);
                    }
                }

                if (bundle.SyncNetwork)
                    SyncToClients();

                _log.Info("Weather applied: " + Capture().Summary
                          + (string.IsNullOrEmpty(bundle.AtmospherePreset) ? "" : " preset=" + bundle.AtmospherePreset));
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                _log.Error("TryApplyBundle failed", ex);
                return false;
            }
            finally
            {
                _applying = false;
            }
        }

        public void SetLock(WeatherBundle bundle, bool enabled)
        {
            lock (_lock)
            {
                _lockEnabled = enabled;
                _lockedBundle = enabled ? bundle : null;
            }

            if (enabled)
            {
                EnsureLockHook();
                if (bundle != null)
                {
                    string err;
                    TryApplyBundle(bundle, out err);
                }
                _log.Info("WeatherService lock ON");
            }
            else
            {
                _log.Info("WeatherService lock OFF");
            }
        }

        public void SyncToClients()
        {
            try
            {
                if (Main.netMode == 2)
                    NetMessage.SendData(7); // WorldData
            }
            catch (Exception ex)
            {
                _log.Error("WeatherService SyncToClients failed", ex);
            }
        }

        private bool TrySetNoAuthCheck(string id, WeatherValue value, WeatherSetOptions options, out string error)
        {
            error = null;
            IWeatherChannel ch;
            if (!TryGet(id, out ch) || ch == null)
            {
                error = "Unknown channel: " + id;
                return false;
            }
            return ch.TryWrite(value, options, out error);
        }

        private void RegisterInternal(IWeatherChannel channel)
        {
            _channels[channel.Id] = channel;
        }

        private void RebuildOrdered()
        {
            _ordered.Clear();
            _ordered.AddRange(
                _channels.Values
                    .OrderBy(c => (int)c.Category)
                    .ThenBy(c => c.Id, StringComparer.OrdinalIgnoreCase));
        }

        private void EnsureLockHook()
        {
            if (_harmony != null)
                return;
            try
            {
                _harmony = new Harmony("timf.core.weather");
                var m = AccessTools.Method(typeof(Main), "UpdateWeather", new[] { typeof(GameTime), typeof(int) });
                if (m == null)
                    m = AccessTools.Method(typeof(Main), "UpdateTime", Type.EmptyTypes);
                if (m != null)
                {
                    _harmony.Patch(m, postfix: new HarmonyMethod(typeof(WeatherService), nameof(LockPostfix)));
                    LockPostfixHolder.Service = this;
                    _log.Info("WeatherService lock hook installed on " + m.Name);
                }
                else
                    _log.Error("WeatherService: no UpdateWeather/UpdateTime to patch");
            }
            catch (Exception ex)
            {
                _log.Error("WeatherService lock hook failed", ex);
            }
        }

        /// <summary>Harmony postfix trampoline (static → instance).</summary>
        private static void LockPostfix()
        {
            try { LockPostfixHolder.Service?.OnWeatherTick(); }
            catch { /* never break weather */ }
        }

        private void OnWeatherTick()
        {
            WeatherBundle bundle;
            lock (_lock)
            {
                if (!_lockEnabled || _lockedBundle == null)
                    return;
                bundle = _lockedBundle;
            }

            if (!IsAuthority())
                return;

            // Re-apply without spamming network every tick.
            var copy = CloneBundle(bundle);
            copy.SyncNetwork = false;
            string err;
            TryApplyBundle(copy, out err);

            if (++_syncCooldown >= 120)
            {
                _syncCooldown = 0;
                SyncToClients();
            }
        }

        private static WeatherBundle CloneBundle(WeatherBundle b)
        {
            return new WeatherBundle
            {
                AtmospherePreset = b.AtmospherePreset,
                RainIntensity = b.RainIntensity,
                WindSpeed = b.WindSpeed,
                MoonPhase = b.MoonPhase,
                EnableEvents = b.EnableEvents != null ? new List<string>(b.EnableEvents) : new List<string>(),
                DisableEvents = b.DisableEvents != null ? new List<string>(b.DisableEvents) : new List<string>(),
                Instant = b.Instant,
                SyncNetwork = b.SyncNetwork,
            };
        }

        private static bool IsAuthority()
        {
            try
            {
                if (Main.dedServ) return true;
                return Main.netMode != 1;
            }
            catch { return false; }
        }

        private static bool SandstormHappening()
        {
            try { return Terraria.GameContent.Events.Sandstorm.Happening; }
            catch { return false; }
        }

        private static bool LanternUp()
        {
            try { return Terraria.GameContent.Events.LanternNight.LanternsUp; }
            catch { return false; }
        }

        private static string BuildSummary(WeatherSnapshot s)
        {
            var rain = s.Raining ? ("rain=" + s.RainIntensity.ToString("0.00")) : "clear";
            var sand = s.Sandstorm ? " sandstorm" : "";
            var slime = s.SlimeRain ? " slime" : "";
            return rain + sand + slime + " wind=" + s.WindSpeed.ToString("0.00") + " moon=" + s.MoonPhase;
        }

        private static class LockPostfixHolder
        {
            public static WeatherService Service;
        }
    }
}
