using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BepInEx;

namespace Qmod
{
    internal static partial class ThorTp
    {
        private const string StoreFileName = "com.aeons.qmod.tp.txt";
        private const string LegacyStoreFileName = "com.aeons.qmod.tp.json";

        private static string cachedWorld;
        private static List<TpPoint> cachedPoints;

        private class TpPoint
        {
            public string name = "";
            public float x;
            public float y;
            public float z;
            public float yaw;
        }

        private static List<TpPoint> LoadCurrentPoints()
        {
            string world = CurrentWorld();
            if (cachedPoints != null && cachedWorld == world)
            {
                return new List<TpPoint>(cachedPoints);
            }

            Dictionary<string, List<TpPoint>> worlds = ReadFile();
            List<TpPoint> points;
            if (!worlds.TryGetValue(world, out points) || points == null)
            {
                points = new List<TpPoint>();
            }

            cachedWorld = world;
            cachedPoints = new List<TpPoint>(points);
            return new List<TpPoint>(cachedPoints);
        }

        private static void SaveCurrentPoints(List<TpPoint> points)
        {
            string world = CurrentWorld();
            cachedWorld = world;
            cachedPoints = new List<TpPoint>(points);

            Dictionary<string, List<TpPoint>> worlds = ReadFile();
            worlds[world] = new List<TpPoint>(points);
            try
            {
                File.WriteAllText(SavePath(), Encode(worlds));
            }
            catch (Exception e)
            {
                Jotunn.Logger.LogWarning("Impossible d'écrire les destinations TP: " + e.Message);
            }
        }

        private static Dictionary<string, List<TpPoint>> ReadFile()
        {
            Dictionary<string, List<TpPoint>> worlds = new Dictionary<string, List<TpPoint>>();
            string path = SavePath();
            if (!File.Exists(path))
            {
                TryCopyLegacy(path);
            }

            if (!File.Exists(path))
            {
                return worlds;
            }

            try
            {
                string current = "default";
                foreach (string raw in File.ReadAllLines(path))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line[0] == '#')
                    {
                        continue;
                    }

                    string[] parts = line.Split('\t');
                    if (parts.Length >= 2 && parts[0] == "W")
                    {
                        current = Unescape(parts[1]);
                        if (!worlds.ContainsKey(current))
                        {
                            worlds[current] = new List<TpPoint>();
                        }

                        continue;
                    }

                    if (parts.Length >= 6 && parts[0] == "P")
                    {
                        if (!worlds.ContainsKey(current))
                        {
                            worlds[current] = new List<TpPoint>();
                        }

                        worlds[current].Add(new TpPoint
                        {
                            name = Unescape(parts[1]),
                            x = ParseFloat(parts[2]),
                            y = ParseFloat(parts[3]),
                            z = ParseFloat(parts[4]),
                            yaw = ParseFloat(parts[5])
                        });
                    }
                }
            }
            catch (Exception e)
            {
                Jotunn.Logger.LogWarning("Impossible de lire les destinations TP: " + e.Message);
            }

            return worlds;
        }

        private static string Encode(Dictionary<string, List<TpPoint>> worlds)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("# Qmod TP\n");
            foreach (KeyValuePair<string, List<TpPoint>> pair in worlds)
            {
                builder.Append("W\t").Append(Escape(pair.Key)).Append('\n');
                if (pair.Value == null)
                {
                    continue;
                }

                for (int i = 0; i < pair.Value.Count; i++)
                {
                    TpPoint point = pair.Value[i];
                    builder.Append("P\t").Append(Escape(point.name)).Append('\t')
                        .Append(FormatFloat(point.x)).Append('\t')
                        .Append(FormatFloat(point.y)).Append('\t')
                        .Append(FormatFloat(point.z)).Append('\t')
                        .Append(FormatFloat(point.yaw)).Append('\n');
                }
            }

            return builder.ToString();
        }

        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "";
            }

            return value.Replace("\t", " ").Replace("\r", " ").Replace("\n", " ");
        }

        private static string Unescape(string value)
        {
            return value ?? "";
        }

        private static string FormatFloat(float value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static float ParseFloat(string value)
        {
            float parsed;
            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) ? parsed : 0f;
        }

        private static string CurrentWorld()
        {
            if (ZNet.instance)
            {
                string name = ZNet.instance.GetWorldName();
                if (!string.IsNullOrEmpty(name))
                {
                    return name;
                }
            }

            return "default";
        }

        private static string SavePath()
        {
            return Path.Combine(SaveDirectory(), StoreFileName);
        }

        private static string SaveDirectory()
        {
            string directory = Qmod.Instance && Qmod.Instance.Config != null
                ? Path.GetDirectoryName(Qmod.Instance.Config.ConfigFilePath)
                : Paths.ConfigPath;
            if (string.IsNullOrEmpty(directory))
            {
                directory = Paths.ConfigPath;
            }

            return directory;
        }

        private static void TryCopyLegacy(string path)
        {
            try
            {
                string legacy = Path.Combine(SaveDirectory(), LegacyStoreFileName);
                if (File.Exists(legacy))
                {
                    File.Copy(legacy, path);
                }
            }
            catch (Exception e)
            {
                Jotunn.Logger.LogWarning("Impossible de migrer les destinations TP: " + e.Message);
            }
        }
    }
}
