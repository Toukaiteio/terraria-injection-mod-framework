// tModLoader's AssemblyManager.VerifyMod requires at least one type in the mod assembly
// whose namespace starts with the mod's internal (folder) name — here "TimfBridge".
// The bridge's own code intentionally lives under the TIMF.* namespaces
// (TIMF.Bridge / TIMF.Abstractions / TIMF.UI) so client mods can consume a stable
// "TIMF.Abstractions" API surface. This otherwise-unused marker supplies a type in the
// TimfBridge namespace purely to satisfy that loader check.
namespace TimfBridge
{
    internal static class NamespaceMarker
    {
    }
}
