using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace WorldMapIcons
{
    internal sealed class WorldMapIconsConfig
    {
        public bool Enabled = true;

        public bool DrawNPCs = true;
        public bool DrawItems = true;
        public bool DrawProjectiles = false;

        // Draw even in unexplored (un-revealed) map areas.
        public bool DrawNPCsIfNotExplored = false;
        public bool DrawItemsIfNotExplored = false;
        public bool DrawProjectilesIfNotExplored = false;

        public float NpcScale = 0.6f;
        public float ItemScale = 1.1f;
        public float ProjectileScale = 0.7f;

        // Cull oversized sprites (in source pixels) so giant sheets don't cover the map.
        public float NpcCullWidth = 150f;
        public float NpcCullHeight = 150f;

        // -1 = unlimited. Otherwise, only entities within this many tiles of the player.
        public float DrawDistance = 160f;

        public static WorldMapIconsConfig LoadOrCreate(string path)
        {
            if (!File.Exists(path))
            {
                var c = new WorldMapIconsConfig();
                c.Save(path);
                return c;
            }

            var cfg = new WorldMapIconsConfig();
            try
            {
                var t = File.ReadAllText(path);
                cfg.Enabled = ReadBool(t, "Enabled", cfg.Enabled);
                cfg.DrawNPCs = ReadBool(t, "DrawNPCs", cfg.DrawNPCs);
                cfg.DrawItems = ReadBool(t, "DrawItems", cfg.DrawItems);
                cfg.DrawProjectiles = ReadBool(t, "DrawProjectiles", cfg.DrawProjectiles);
                cfg.DrawNPCsIfNotExplored = ReadBool(t, "DrawNPCsIfNotExplored", cfg.DrawNPCsIfNotExplored);
                cfg.DrawItemsIfNotExplored = ReadBool(t, "DrawItemsIfNotExplored", cfg.DrawItemsIfNotExplored);
                cfg.DrawProjectilesIfNotExplored = ReadBool(t, "DrawProjectilesIfNotExplored", cfg.DrawProjectilesIfNotExplored);
                cfg.NpcScale = ReadFloat(t, "NpcScale", cfg.NpcScale);
                cfg.ItemScale = ReadFloat(t, "ItemScale", cfg.ItemScale);
                cfg.ProjectileScale = ReadFloat(t, "ProjectileScale", cfg.ProjectileScale);
                cfg.NpcCullWidth = ReadFloat(t, "NpcCullWidth", cfg.NpcCullWidth);
                cfg.NpcCullHeight = ReadFloat(t, "NpcCullHeight", cfg.NpcCullHeight);
                cfg.DrawDistance = ReadFloat(t, "DrawDistance", cfg.DrawDistance);
            }
            catch
            {
                // keep defaults
            }

            return cfg;
        }

        public void Save(string path)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"Enabled\": " + B(Enabled) + ",");
            sb.AppendLine("  \"DrawNPCs\": " + B(DrawNPCs) + ",");
            sb.AppendLine("  \"DrawItems\": " + B(DrawItems) + ",");
            sb.AppendLine("  \"DrawProjectiles\": " + B(DrawProjectiles) + ",");
            sb.AppendLine("  \"DrawNPCsIfNotExplored\": " + B(DrawNPCsIfNotExplored) + ",");
            sb.AppendLine("  \"DrawItemsIfNotExplored\": " + B(DrawItemsIfNotExplored) + ",");
            sb.AppendLine("  \"DrawProjectilesIfNotExplored\": " + B(DrawProjectilesIfNotExplored) + ",");
            sb.AppendLine("  \"NpcScale\": " + F(NpcScale) + ",");
            sb.AppendLine("  \"ItemScale\": " + F(ItemScale) + ",");
            sb.AppendLine("  \"ProjectileScale\": " + F(ProjectileScale) + ",");
            sb.AppendLine("  \"NpcCullWidth\": " + F(NpcCullWidth) + ",");
            sb.AppendLine("  \"NpcCullHeight\": " + F(NpcCullHeight) + ",");
            sb.AppendLine("  \"DrawDistance\": " + F(DrawDistance));
            sb.AppendLine("}");
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }

        private static string B(bool b) => b ? "true" : "false";
        private static string F(float f) => f.ToString(CultureInfo.InvariantCulture);

        private static bool ReadBool(string json, string key, bool fallback)
        {
            var s = ReadRaw(json, key);
            if (s == null) return fallback;
            s = s.Trim().Trim(',').Trim();
            if (s.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
            if (s.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
            return fallback;
        }

        private static float ReadFloat(string json, string key, float fallback)
        {
            var s = ReadRaw(json, key);
            if (s == null) return fallback;
            s = s.Trim().Trim(',').Trim().Trim('"');
            float v;
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : fallback;
        }

        private static string ReadRaw(string json, string key)
        {
            var token = "\"" + key + "\"";
            var i = json.IndexOf(token, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return null;
            i = json.IndexOf(':', i);
            if (i < 0) return null;
            var j = i + 1;
            while (j < json.Length && char.IsWhiteSpace(json[j])) j++;
            if (j >= json.Length) return null;
            if (json[j] == '"')
            {
                var k = json.IndexOf('"', j + 1);
                if (k < 0) return null;
                return json.Substring(j, k - j + 1);
            }

            var end = j;
            while (end < json.Length && json[end] != ',' && json[end] != '}' && json[end] != '\n' && json[end] != '\r')
                end++;
            return json.Substring(j, end - j);
        }
    }
}
