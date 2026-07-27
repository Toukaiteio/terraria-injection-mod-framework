namespace TIMF.Abstractions.Security
{
    public enum SensitiveOperationStatus
    {
        Pending = 0,
        Granted = 1,
        Denied = 2,
        Consumed = 3,
        Cancelled = 4,
    }
}
