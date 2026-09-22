using System;
using System.Collections.Generic;
using System.IO;

namespace Qmod
{
    // Migration one-shot du .cfg après le renommage des sections (1.0.45).
    // Pur texte et idempotent : ne réécrit le fichier que si un ancien
    // header ou une clé à déplacer est trouvé. Backup .bak avant écriture.
    internal static class ConfigMigration
    {
        private const string LegacyKeybinds = "Keybinds";

        private static readonly Dictionary<string, string> SectionRenames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "0. Graphiques", "01. Graphiques" },
            { "0. Nuage magique", "02. Nuage magique" },
            { "Eclair", "03. Eclair" },
            { "Craft", "04. Craft" },
            { "Farming", "05. Farming" },
            { "HUD", "06. HUD" },
            { "Camera", "07. Camera" }
        };

        private static readonly Dictionary<string, string> KeybindMoves = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "ToggleSupersampling", "01. Graphiques" },
            { "ToggleWaterShader", "01. Graphiques" },
            { "ToggleCultivateHarvest", "05. Farming" },
            { "SpawnBoar", "05. Farming" },
            { "ToggleStatusHud", "06. HUD" },
            { "ToggleUnarmedHudHide", "06. HUD" },
            { "ToggleHugin", "06. HUD" },
            { "ToggleYoteiCamera", "07. Camera" },
            { "ToggleCinematicIdle", "07. Camera" },
            { "StartCinematic", "07. Camera" },
            { "SwapShoulder", "07. Camera" }
        };

        internal static bool MigrateFile(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return false;
            }

            string[] original;
            try
            {
                original = File.ReadAllLines(path);
            }
            catch (Exception)
            {
                return false;
            }

            bool changed;
            string[] migrated = MigrateLines(original, out changed);
            if (!changed)
            {
                return false;
            }

            try
            {
                File.Copy(path, path + ".bak", true);
                File.WriteAllLines(path, migrated);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        internal static string[] MigrateLines(string[] lines, out bool changed)
        {
            changed = false;
            if (lines == null)
            {
                return new string[0];
            }

            List<Section> sections = new List<Section>();
            Section preamble = new Section { Name = null };
            Section current = null;
            List<string> pending = new List<string>();

            foreach (string raw in lines)
            {
                string line = raw ?? "";
                string trimmed = line.Trim();
                string header = ParseHeader(trimmed);
                if (header != null)
                {
                    FlushPending(preamble, current, pending);
                    current = new Section { Name = header };
                    sections.Add(current);
                    continue;
                }

                if (trimmed.Length == 0)
                {
                    continue;
                }

                if (trimmed[0] == '#')
                {
                    pending.Add(line);
                    continue;
                }

                List<string> block = new List<string>(pending);
                pending.Clear();
                block.Add(line);

                string target = MoveTarget(current, KeyOf(trimmed));
                if (target != null)
                {
                    changed = true;
                    FindOrCreate(sections, target).Blocks.Add(block);
                }
                else if (current != null)
                {
                    current.Blocks.Add(block);
                }
                else
                {
                    preamble.Blocks.Add(block);
                }
            }

            FlushPending(preamble, current, pending);

            List<string> output = new List<string>();
            foreach (List<string> block in preamble.Blocks)
            {
                output.AddRange(block);
                output.Add("");
            }

            foreach (Section section in sections)
            {
                if (section.Blocks.Count == 0)
                {
                    continue;
                }

                string emit = EmittedName(section.Name);
                if (!string.Equals(emit, section.Name, StringComparison.Ordinal))
                {
                    changed = true;
                }

                output.Add("[" + emit + "]");
                output.Add("");
                foreach (List<string> block in section.Blocks)
                {
                    output.AddRange(block);
                    output.Add("");
                }
            }

            while (output.Count > 0 && output[output.Count - 1] == "")
            {
                output.RemoveAt(output.Count - 1);
            }

            return output.ToArray();
        }

        private static void FlushPending(Section preamble, Section current, List<string> pending)
        {
            if (pending.Count == 0)
            {
                return;
            }

            List<string> block = new List<string>(pending);
            pending.Clear();
            if (current != null)
            {
                current.Blocks.Add(block);
            }
            else
            {
                preamble.Blocks.Add(block);
            }
        }

        private static string MoveTarget(Section current, string key)
        {
            if (current == null || key.Length == 0)
            {
                return null;
            }

            if (!string.Equals(current.Name, LegacyKeybinds, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            string target;
            return KeybindMoves.TryGetValue(key, out target) ? target : null;
        }

        private static Section FindOrCreate(List<Section> sections, string name)
        {
            for (int i = 0; i < sections.Count; i++)
            {
                if (string.Equals(EmittedName(sections[i].Name), name, StringComparison.OrdinalIgnoreCase))
                {
                    return sections[i];
                }
            }

            Section created = new Section { Name = name };
            sections.Add(created);
            return created;
        }

        private static string EmittedName(string name)
        {
            string renamed;
            return SectionRenames.TryGetValue(name, out renamed) ? renamed : name;
        }

        private static string ParseHeader(string trimmed)
        {
            if (trimmed.Length >= 2 && trimmed[0] == '[' && trimmed[trimmed.Length - 1] == ']')
            {
                return trimmed.Substring(1, trimmed.Length - 2).Trim();
            }

            return null;
        }

        private static string KeyOf(string trimmed)
        {
            int eq = trimmed.IndexOf('=');
            return eq < 0 ? "" : trimmed.Substring(0, eq).Trim();
        }

        private class Section
        {
            public string Name;
            public readonly List<List<string>> Blocks = new List<List<string>>();
        }
    }
}
