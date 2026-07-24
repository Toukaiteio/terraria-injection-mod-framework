using System;
using TIMF.Abstractions;

namespace TIMF.Core.Modding
{
    /// <summary>
    /// Infers <see cref="TimfSide"/> from capability interfaces and validates
    /// against an optional explicit <see cref="TimfModAttribute.Side"/>.
    /// </summary>
    internal static class SideClassifier
    {
        public sealed class Result
        {
            public TimfSide Side;
            public bool RequiredOnJoin;
            public string FailReason;
            public bool HasClientCapability;
            public bool HasAuthorityCapability;
            public bool IsVanillaPlugin;
            public TimfSide InferredSide;
            public bool UsedExplicitSide;
        }

        public static Result Classify(Type entryType, TimfModAttribute attr)
        {
            var r = new Result();
            if (entryType == null)
            {
                r.FailReason = "Entry type is null";
                r.Side = TimfSide.Client;
                return r;
            }

            r.HasClientCapability =
                typeof(IClientMod).IsAssignableFrom(entryType)
                || typeof(IPlayerUpdateHook).IsAssignableFrom(entryType)
                || typeof(IMapOverlayHook).IsAssignableFrom(entryType)
                || typeof(IInfoAccessoryHook).IsAssignableFrom(entryType);

            r.IsVanillaPlugin = typeof(IVanillaPlugin).IsAssignableFrom(entryType);

            r.HasAuthorityCapability =
                r.IsVanillaPlugin
                || typeof(IAuthorityMod).IsAssignableFrom(entryType)
                || typeof(IServerMod).IsAssignableFrom(entryType);

            // Infer
            if (r.IsVanillaPlugin)
                r.InferredSide = TimfSide.Plugin;
            else if (r.HasClientCapability && r.HasAuthorityCapability)
                r.InferredSide = TimfSide.Both;
            else if (r.HasAuthorityCapability)
                r.InferredSide = TimfSide.Server;
            else
                r.InferredSide = TimfSide.Client;

            r.Side = r.InferredSide;
            r.UsedExplicitSide = attr != null && attr.SideSpecified;

            if (r.UsedExplicitSide)
            {
                var explicitSide = attr.Side;
                if (!IsCompatible(explicitSide, r.InferredSide, r.HasClientCapability, r.HasAuthorityCapability, r.IsVanillaPlugin))
                {
                    r.FailReason =
                        "Side mismatch: [TimfMod(Side=" + explicitSide + ")] is incompatible with "
                        + "capabilities (inferred " + r.InferredSide
                        + "; clientCaps=" + r.HasClientCapability
                        + ", authorityCaps=" + r.HasAuthorityCapability
                        + ", vanillaPlugin=" + r.IsVanillaPlugin + "). "
                        + "Implement IClientMod / IAuthorityMod / IVanillaPlugin consistently, or fix Side=.";
                    r.Side = explicitSide;
                    return r;
                }

                r.Side = explicitSide;
            }

            // RequiredOnJoin
            if (r.Side == TimfSide.Plugin || r.IsVanillaPlugin)
                r.RequiredOnJoin = false;
            else if (TimfSides.ParticipatesInHandshake(r.Side))
                r.RequiredOnJoin = attr == null || attr.RequiredOnJoin;
            else
                r.RequiredOnJoin = false;

            // Soft capability warnings encoded only when truly empty markers for authority-looking sides
            if (r.Side == TimfSide.Plugin && !r.IsVanillaPlugin && r.UsedExplicitSide)
            {
                // Explicit Plugin without IVanillaPlugin is allowed but recommend the interface.
                // Not a hard fail.
            }

            if (r.Side == TimfSide.Client && r.HasAuthorityCapability)
            {
                r.FailReason =
                    "Client side cannot implement authority capabilities (IAuthorityMod / IServerMod / IVanillaPlugin).";
                return r;
            }

            if (r.Side == TimfSide.Plugin && r.HasClientCapability && !r.UsedExplicitSide)
            {
                // IVanillaPlugin + client hooks without explicit Side → force Both is wrong for plugin.
                // Prefer Plugin and strip client? Better fail so author chooses.
                r.FailReason =
                    "IVanillaPlugin cannot combine with client-only hooks (IPlayerUpdateHook / map / info). "
                    + "Split into a Client mod + Plugin, or use TimfSide.Both with handshake.";
                return r;
            }

            if (r.Side == TimfSide.Plugin && r.HasClientCapability && r.UsedExplicitSide && explicitIsPlugin(attr))
            {
                r.FailReason =
                    "TimfSide.Plugin forbids client hooks. Use Both (handshake) or split mods.";
                return r;
            }

            return r;
        }

        private static bool explicitIsPlugin(TimfModAttribute attr)
        {
            return attr != null && attr.SideSpecified && attr.Side == TimfSide.Plugin;
        }

        private static bool IsCompatible(
            TimfSide explicitSide,
            TimfSide inferred,
            bool clientCaps,
            bool authorityCaps,
            bool vanillaPlugin)
        {
            if (vanillaPlugin)
            {
                // IVanillaPlugin always forces Plugin semantics.
                return explicitSide == TimfSide.Plugin;
            }

            switch (explicitSide)
            {
                case TimfSide.Client:
                    return !authorityCaps;
                case TimfSide.Server:
                    return authorityCaps && !clientCaps;
                case TimfSide.Both:
                    // Both may be declared even if only one side is marked yet (forward-looking),
                    // but if they only have client caps it's OK; if only authority OK.
                    return true;
                case TimfSide.Plugin:
                    return authorityCaps && !clientCaps;
                default:
                    return explicitSide == inferred;
            }
        }
    }
}
