namespace TIMF.Abstractions.Storage
{
    /// <summary>
    /// Framework-owned storage confined to this mod. Configuration names are single file names
    /// under config/mod-data/&lt;ModId&gt;; content paths are read-only and confined to ContentDirectory.
    /// </summary>
    public interface IModStorage
    {
        bool ConfigExists(string name);
        string ReadConfigText(string name);
        void WriteConfigText(string name, string text);

        bool ContentExists(string relativePath);
        byte[] ReadContentBytes(string relativePath);
    }
}
