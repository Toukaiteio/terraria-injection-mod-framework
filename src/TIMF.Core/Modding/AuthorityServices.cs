using TIMF.Abstractions;

namespace TIMF.Core.Modding
{
    internal sealed class AuthorityServices : IAuthorityServices
    {
        public AuthorityServices(IWeatherService weather, IPrefixService prefix)
        {
            Weather = weather;
            Prefix = prefix;
        }

        public bool IsAuthoritative
        {
            get
            {
                try
                {
                    if (Terraria.Main.dedServ)
                        return true;
                    return Terraria.Main.netMode != 1;
                }
                catch
                {
                    return false;
                }
            }
        }

        public IWeatherService Weather { get; }
        public IPrefixService Prefix { get; }
    }
}
