using System;
using TIMF.Abstractions;

namespace TIMF.Core.Modding
{
    /// <summary>
    /// Resolves the two independent axes of a mod:
    /// <see cref="TimfSide"/> (which process role the code belongs to, inferred from the
    /// capability interfaces) and <see cref="TimfNetProfile"/> (whether joining peers need
    /// matching code, declared on <see cref="TimfModAttribute.Net"/>).
    ///
    /// Side is always inferred. An explicit <c>Side=</c> is an assertion checked against the
    /// inference, never an override — so the interfaces stay the single source of truth.
    /// </summary>
    internal static class SideClassifier
    {
        public sealed class Result
        {
            public TimfSide Side;
            public TimfNetProfile NetProfile;
            public string FailReason;
            public bool HasClientCapability;
            public bool HasAuthorityCapability;
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

            // Only IAuthorityMod declares the authority capability. IAuthorityLifecycle is a
            // lifecycle interface and deliberately does not count — otherwise a client mod that
            // just wants activate/deactivate callbacks would silently gain an authority half.
            r.HasAuthorityCapability = typeof(IAuthorityMod).IsAssignableFrom(entryType);

            r.InferredSide = Infer(r.HasClientCapability, r.HasAuthorityCapability);
            r.UsedExplicitSide = attr != null && attr.SideSpecified;
            r.Side = r.InferredSide;
            r.NetProfile = attr != null ? attr.Net : TimfNetProfile.Vanilla;

            r.FailReason = Validate(r, attr);
            return r;
        }

        private static TimfSide Infer(bool clientCaps, bool authorityCaps)
        {
            var side = TimfSide.None;
            if (clientCaps)
                side |= TimfSide.Client;
            if (authorityCaps)
                side |= TimfSide.Authority;

            // A bare IMod with no capability interface is a plain client mod.
            return side == TimfSide.None ? TimfSide.Client : side;
        }

        /// <summary>Returns null when valid.</summary>
        private static string Validate(Result r, TimfModAttribute attr)
        {
            // TimfSide.Client used to be 0, which is now None. A stale DLL built against the
            // pre-flags abstractions lands here, so say so instead of reporting a bare mismatch.
            if (r.UsedExplicitSide && attr.Side == TimfSide.None)
            {
                return "[TimfMod(Side=None)] is not a valid declaration. If this mod was built "
                       + "against TIMF before TimfSide became a flags enum, rebuild it: the old "
                       + "TimfSide.Client had value 0, which now reads as None.";
            }

            if (r.UsedExplicitSide && attr.Side != r.InferredSide)
            {
                return "Side mismatch: [TimfMod(Side=" + attr.Side + ")] contradicts the implemented "
                       + "interfaces (inferred " + r.InferredSide + "). "
                       + "Side is an assertion, not an override — implement IClientMod / IAuthorityMod "
                       + "to match, or drop the Side= argument.";
            }

            // The handshake exists to guarantee matching world logic on both peers. Without an
            // authority half there is nothing to negotiate.
            if (r.NetProfile != TimfNetProfile.Vanilla && !TimfSides.IsAuthorityCapable(r.Side))
            {
                return "Net=" + r.NetProfile + " requires an authority half. "
                       + "Implement IAuthorityMod, or leave Net at TimfNetProfile.Vanilla.";
            }

            return null;
        }
    }
}
