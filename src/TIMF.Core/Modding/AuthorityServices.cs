using TIMF.Abstractions;

namespace TIMF.Core.Modding
{
    internal sealed class AuthorityServices : IAuthorityServices
    {
        public bool IsAuthoritative
        {
            get
            {
                try
                {
                    if (Terraria.Main.dedServ)
                        return true;
                    // 0 = SP, 2 = listen host; 1 = multiplayer client (not authority).
                    return Terraria.Main.netMode != 1;
                }
                catch
                {
                    return false;
                }
            }
        }
    }
}
