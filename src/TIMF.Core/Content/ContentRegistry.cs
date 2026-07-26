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

        public ContentRegistry(
            ILogger log,
            string modId,
            List<TimfItem> itemSink,
            List<TimfTile> tileSink,
            List<TimfWall> wallSink)
        {
            _log = log;
            ModId = modId;
            _itemSink = itemSink;
            _tileSink = tileSink;
            _wallSink = wallSink;
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
    }
}
