using System;
using System.Collections.Generic;
using TIMF.Abstractions;
using TIMF.Content;

namespace TIMF.Core.Content
{
    /// <summary>
    /// Per-mod view handed to <see cref="IContentMod.AddContent"/>. One instance is created
    /// per mod so registrations are automatically attributed to the right owner.
    /// </summary>
    internal sealed class ContentRegistry : IContentRegistry
    {
        private readonly ILogger _log;
        private readonly List<TimfItem> _itemSink;
        private readonly List<TimfTile> _tileSink;
        private readonly List<TimfWall> _wallSink;
        private readonly List<TimfNpc> _npcSink;
        private readonly List<TimfBiome> _biomeSink;
        private readonly List<TimfProjectile> _projectileSink;
        private readonly List<TimfBuff> _buffSink;

        public ContentRegistry(
            ILogger log,
            string modId,
            List<TimfItem> itemSink,
            List<TimfTile> tileSink,
            List<TimfWall> wallSink,
            List<TimfNpc> npcSink,
            List<TimfBiome> biomeSink,
            List<TimfProjectile> projectileSink,
            List<TimfBuff> buffSink)
        {
            _log = log;
            ModId = modId;
            _itemSink = itemSink;
            _tileSink = tileSink;
            _wallSink = wallSink;
            _npcSink = npcSink;
            _biomeSink = biomeSink;
            _projectileSink = projectileSink;
            _buffSink = buffSink;
        }

        public string ModId { get; }

        public void AddItem<TItem>() where TItem : TimfItem, new()
        {
            AddItem(new TItem());
        }

        public void AddItem(TimfItem item)
        {
            if (item == null)
                throw new ArgumentNullException(nameof(item));

            item.ModId = ModId;

            if (string.IsNullOrWhiteSpace(item.InternalName))
                throw new InvalidOperationException(item.GetType().FullName + ".InternalName is empty");

            _itemSink.Add(item);
            _log.Debug("Content: registered item definition " + item.ContentKey);
        }

        public void AddTile<TTile>() where TTile : TimfTile, new()
        {
            AddTile(new TTile());
        }

        public void AddTile(TimfTile tile)
        {
            if (tile == null)
                throw new ArgumentNullException(nameof(tile));

            tile.ModId = ModId;
            if (string.IsNullOrWhiteSpace(tile.InternalName))
                throw new InvalidOperationException(tile.GetType().FullName + ".InternalName is empty");

            _tileSink.Add(tile);
            _log.Debug("Content: registered tile definition " + tile.ContentKey);
        }

        public void AddWall<TWall>() where TWall : TimfWall, new()
        {
            AddWall(new TWall());
        }

        public void AddWall(TimfWall wall)
        {
            if (wall == null)
                throw new ArgumentNullException(nameof(wall));
            wall.ModId = ModId;
            if (string.IsNullOrWhiteSpace(wall.InternalName))
                throw new InvalidOperationException(wall.GetType().FullName + ".InternalName is empty");
            _wallSink.Add(wall);
            _log.Debug("Content: registered wall definition " + wall.ContentKey);
        }

        public void AddNpc<TNpc>() where TNpc : TimfNpc, new() { AddNpc(new TNpc()); }

        public void AddNpc(TimfNpc npc)
        {
            if (npc == null) throw new ArgumentNullException(nameof(npc));
            npc.ModId = ModId;
            if (string.IsNullOrWhiteSpace(npc.InternalName))
                throw new InvalidOperationException(npc.GetType().FullName + ".InternalName is empty");
            _npcSink.Add(npc);
            _log.Debug("Content: registered NPC definition " + npc.ContentKey);
        }

        public void AddBiome<TBiome>() where TBiome : TimfBiome, new() { AddBiome(new TBiome()); }

        public void AddBiome(TimfBiome biome)
        {
            if (biome == null) throw new ArgumentNullException(nameof(biome));
            biome.ModId = ModId;
            if (string.IsNullOrWhiteSpace(biome.InternalName))
                throw new InvalidOperationException(biome.GetType().FullName + ".InternalName is empty");
            _biomeSink.Add(biome);
            _log.Debug("Content: registered biome definition " + biome.ContentKey);
        }

        public void AddProjectile<TProjectile>() where TProjectile : TimfProjectile, new()
        { AddProjectile(new TProjectile()); }

        public void AddProjectile(TimfProjectile projectile)
        {
            if (projectile == null) throw new ArgumentNullException(nameof(projectile));
            projectile.ModId = ModId;
            if (string.IsNullOrWhiteSpace(projectile.InternalName))
                throw new InvalidOperationException(projectile.GetType().FullName + ".InternalName is empty");
            _projectileSink.Add(projectile);
            _log.Debug("Content: registered projectile definition " + projectile.ContentKey);
        }

        public void AddBuff<TBuff>() where TBuff : TimfBuff, new() { AddBuff(new TBuff()); }

        public void AddBuff(TimfBuff buff)
        {
            if (buff == null) throw new ArgumentNullException(nameof(buff));
            buff.ModId = ModId;
            if (string.IsNullOrWhiteSpace(buff.InternalName))
                throw new InvalidOperationException(buff.GetType().FullName + ".InternalName is empty");
            _buffSink.Add(buff);
            _log.Debug("Content: registered buff definition " + buff.ContentKey);
        }
    }
}
