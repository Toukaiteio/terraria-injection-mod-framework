using System;

namespace TIMF.Abstractions.Security
{
    public sealed class SensitiveOperationRequest
    {
        public string Id { get; internal set; }
        public string ModId { get; internal set; }
        public SensitiveOperationKind Kind { get; internal set; }
        public string Target { get; internal set; }
        public string Arguments { get; internal set; }
        public string WorkingDirectory { get; internal set; }
        public string Purpose { get; internal set; }
        public SensitiveOperationStatus Status { get; internal set; }
        public SensitiveAuthorizationScope? GrantedScope { get; internal set; }
        public string DecisionReason { get; internal set; }
        public DateTime CreatedUtc { get; internal set; }
    }
}
