namespace TIMF.Abstractions.Security
{
    public sealed class SensitiveProcessResult
    {
        public int ExitCode { get; internal set; }
        public string StandardOutput { get; internal set; }
        public string StandardError { get; internal set; }
    }
}
