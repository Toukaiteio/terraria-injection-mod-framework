namespace TIMF.Pinyin
{
    /// <summary>
    /// Shared Chinese-pinyin search helper, published by the <c>TIMF.Pinyin</c> library mod so
    /// multiple mods can offer pinyin-aware search without each bundling the pinyin dataset.
    ///
    /// Declared in this assembly (not TIMF.Abstractions) on purpose: the security audit rejects
    /// raw <c>IServiceRegistry.Register</c> from untrusted mods, and the sanctioned
    /// <c>IModServicePublisher.Publish</c> only accepts interfaces declared by the publishing
    /// mod's own assembly. Consumers reference TIMF.Pinyin.dll (Private=false), depend with
    /// <c>[TimfDependsOn("TIMF.Pinyin")]</c>, and resolve via
    /// <c>context.Services.TryGetService(out IPinyinService pinyin)</c> in <c>Load</c> —
    /// the loader's AssemblyResolve probes every Mods folder, so the reference binds at runtime.
    ///
    /// Typical use: precompute <see cref="ToPinyin"/> + <see cref="ToInitials"/> once per entry
    /// while building a search index, then call <see cref="Matches"/> per keystroke. Non-CJK text
    /// passes through unchanged; if the service is absent, fall back to plain substring matching.
    /// </summary>
    public interface IPinyinService
    {
        /// <summary>Full pinyin, lowercase, no spaces or tones. e.g. "火把" → "huoba".</summary>
        string ToPinyin(string text);

        /// <summary>First-letter initials, lowercase. e.g. "火把" → "hb".</summary>
        string ToInitials(string text);

        /// <summary>
        /// True when <paramref name="queryLower"/> (already lowercased) matches the entry by
        /// display name, precomputed full pinyin, or initials. Empty query matches everything.
        /// </summary>
        bool Matches(string name, string nameLower, string pinyin, string initials, string queryLower);
    }
}
