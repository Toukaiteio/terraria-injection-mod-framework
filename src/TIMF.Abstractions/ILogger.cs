namespace TIMF.Abstractions
{
    public interface ILogger
    {
        void Info(string message);
        void Warn(string message);
        void Error(string message);
        void Error(string message, System.Exception exception);
        void Debug(string message);
    }
}
