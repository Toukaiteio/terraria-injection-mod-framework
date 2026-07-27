namespace TIMF.Abstractions.Security
{
    /// <summary>Read-only security-center status plus a request to show its framework-owned UI.</summary>
    public interface ISecurityCenter
    {
        int PendingRequestCount { get; }
        int PersistentGrantCount { get; }
        int BlockedModCount { get; }
        string BoundaryWarning { get; }
        void Show();
    }
}
