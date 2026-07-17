using System.Runtime.InteropServices;

namespace TIMF.Core
{
    /// <summary>
    /// Instance entry used by the ICorRuntimeHost CreateInstanceFrom fallback path.
    /// AutoDual so native IDispatch can call Run(string).
    /// </summary>
    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.AutoDual)]
    public class EntryPoint
    {
        public int Run(string home)
        {
            return Loader.Initialize(home);
        }
    }
}
