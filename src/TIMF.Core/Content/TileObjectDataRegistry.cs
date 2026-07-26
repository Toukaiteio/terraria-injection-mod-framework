using System;
using System.Collections.Generic;
using System.Reflection;
using Terraria.ObjectData;
using TIMF.Abstractions;

namespace TIMF.Core.Content
{
    internal static class TileObjectDataRegistry
    {
        internal static bool CloneTemplate(int targetType, int templateType, ILogger log)
        {
            if (templateType < 0)
                return true;
            try
            {
                var source = TileObjectData.GetTileData(templateType, 0, 0);
                if (source == null)
                    throw new InvalidOperationException("template tile " + templateType + " has no TileObjectData");

                var field = typeof(TileObjectData).GetField("_data",
                    BindingFlags.NonPublic | BindingFlags.Static);
                var data = field?.GetValue(null) as List<TileObjectData>;
                if (data == null)
                    throw new MissingFieldException("TileObjectData._data");
                while (data.Count <= targetType)
                    data.Add(null);

                var clone = new TileObjectData();
                clone.FullCopyFrom(source);
                data[targetType] = clone;
                log.Info("Content: cloned TileObjectData " + templateType + " -> " + targetType);
                return true;
            }
            catch (Exception ex)
            {
                log.Error("Content: TileObjectData template registration failed for tile "
                          + targetType, ex);
                return false;
            }
        }
    }
}
