using System;
using System.Collections.Generic;
using System.IO;

namespace Qmod
{
    // Migration one-shot du .cfg. Pur texte et idempotent : ne réécrit
    // le fichier que si un ancien header ou une clé à déplacer est trouvé.
    // Backup .bak avant écriture. Les sections fusionnées (craft,
    // construction, rayons) sortent en un seul bloc.
    internal static class ConfigMigration
    {
        private const string LegacyKeybinds = "Keybinds";

        private static readonly Dictionary<string, string> SectionRenames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "0. Graphiques", "2 - graphismes" },
            { "01. Graphiques", "2 - graphismes" },
            { "0. Nuage magique", "02. Nuage magique" },
            { "Eclair", "03. Eclair" },
            { "Craft", "0 - options" },
            { "04. Craft", "0 - options" },
            { "08. Construction", "0 - options" },
            { "11. Rayons", "0 - options" },
            { "Farming", "4 - farming" },
            { "05. Farming", "4 - farming" },
            { "HUD", "3 - hud" },
            { "06. HUD", "3 - hud" },
            { "Camera", "1 - camera" },
            { "07. Camera", "1 - camera" },
            { "09. Montures", "5 - montures" },
            { "10. Debug", "6 - debug" }
        };

        private static readonly Dictionary<string, string> KeybindMoves = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "ToggleSupersampling", "2 - graphismes" },
            { "ToggleWaterShader", "2 - graphismes" },
            { "ToggleCultivateHarvest", "4 - farming" },
            { "ToggleStatusHud", "3 - hud" },
            { "ToggleUnarmedHudHide", "3 - hud" },
            { "ToggleHugin", "3 - hud" },
            { "ToggleYoteiCamera", "1 - camera" },
            { "ToggleCinematicIdle", "1 - camera" },
            { "StartCinematic", "1 - camera" },
            { "SwapShoulder", "1 - camera" }
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

            List<Section> emitted = new List<Section>();
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

                Section dest = null;
                for (int i = 0; i < emitted.Count; i++)
                {
                    if (string.Equals(emitted[i].Name, emit, StringComparison.OrdinalIgnoreCase))
                    {
                        dest = emitted[i];
                        break;
                    }
                }

                if (dest == null)
                {
                    dest = new Section { Name = emit };
                    emitted.Add(dest);
                }
                else
                {
                    changed = true;
                }

                dest.Blocks.AddRange(section.Blocks);
            }

            foreach (Section section in emitted)
            {
                output.Add("[" + section.Name + "]");
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
