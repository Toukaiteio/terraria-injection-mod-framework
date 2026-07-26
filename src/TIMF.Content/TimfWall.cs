namespace TIMF.Content
{
    /// <summary>Definition of a custom background wall.</summary>
    public abstract class TimfWall
    {
        public int Type { get; internal set; }
        public string ModId { get; internal set; }
        public virtual string InternalName => GetType().Name;
        public string ContentKey => ModId + "/" + InternalName;
        public virtual string DisplayName => InternalName;
        public virtual string Texture => "Content/" + InternalName;

        /// <summary>Item dropped when the wall is removed with a hammer; zero means none.</summary>
        public virtual int ItemDrop => 0;

        /// <summary>Configure Main.wallHouse[Type], WallID sets, and other wall-id arrays.</summary>
        public virtual void SetStaticDefaults() { }
    }
}
