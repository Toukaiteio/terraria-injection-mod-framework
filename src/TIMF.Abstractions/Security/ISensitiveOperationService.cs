namespace TIMF.Abstractions.Security
{
    /// <summary>
    /// Per-mod, framework-mediated access to sensitive host operations. Requests are inert until
    /// the framework security UI grants their exact normalized target and arguments.
    /// </summary>
    public interface ISensitiveOperationService
    {
        SensitiveOperationRequest RequestFileRead(string path, string purpose);
        SensitiveOperationRequest RequestFileWrite(string path, bool overwrite, string purpose);
        SensitiveOperationRequest RequestProcess(
            string executable, string arguments, string workingDirectory, string purpose);
        SensitiveOperationRequest GetRequest(string requestId);
        void Cancel(string requestId);

        byte[] ReadAllBytes(string requestId);
        void WriteAllBytes(string requestId, byte[] data);
        SensitiveProcessResult RunProcess(string requestId, int timeoutMilliseconds = 30000);
    }
}
