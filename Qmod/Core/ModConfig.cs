using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using UnityEngine;

namespace Qmod
{
    internal static class ModConfig
    {
        private const string Options = "0 - options";
        private const string Camera = "1 - camera";
        private const string Graphics = "2 - graphismes";
        private const string Hud = "3 - hud";
        private const string Farming = "4 - farming";
        private const string Mounts = "5 - montures";
        private const string Debug = "6 - debug";

        internal static ConfigEntry<bool> SupersamplingEnabled;
        internal static ConfigEntry<float> SupersamplingScale;
        internal static ConfigEntry<bool> WaterShaderEnabled;
        internal static ConfigEntry<KeyboardShortcut> ToggleSupersampling;
        internal static ConfigEntry<KeyboardShortcut> ToggleWaterShader;

        internal static ConfigEntry<bool> CraftPullEnabled;
        internal static ConfigEntry<float> CraftPullRadius;
        internal static ConfigEntry<bool> ChestDumpEnabled;
        internal static ConfigEntry<KeyboardShortcut> ChestDump;

        internal static ConfigEntry<string> KillMessage;
        internal static ConfigEntry<bool> CultivateHarvestEnabled;
        internal static ConfigEntry<KeyboardShortcut> ToggleCultivateHarvest;

        internal static ConfigEntry<bool> StatusHudEnabled;
        internal static ConfigEntry<bool> UnarmedHudHideEnabled;
        internal static ConfigEntry<float> UnarmedHudHideDelay;
        internal static ConfigEntry<bool> HuginDisabled;
        internal static ConfigEntry<bool> FireplaceSmokeEnabled;
        internal static ConfigEntry<KeyboardShortcut> ToggleStatusHud;
        internal static ConfigEntry<KeyboardShortcut> ToggleUnarmedHudHide;
        internal static ConfigEntry<KeyboardShortcut> ToggleHugin;

        internal static ConfigEntry<bool> YoteiCameraEnabled;
        internal static ConfigEntry<bool> YoteiAutoShoulder;
        internal static ConfigEntry<float> YoteiShoulderOffset;
        internal static ConfigEntry<float> YoteiDistanceBoost;
        internal static ConfigEntry<float> YoteiSprintDistance;
        internal static ConfigEntry<float> YoteiCombatZoom;
        internal static ConfigEntry<float> YoteiHeightOffset;
        internal static ConfigEntry<float> YoteiSmoothness;
        internal static ConfigEntry<float> YoteiSprintFov;
        internal static ConfigEntry<bool> CinematicIdleEnabled;
        internal static ConfigEntry<float> CinematicIdleDelay;
        internal static ConfigEntry<float> CinematicSubjectRadius;
        internal static ConfigEntry<KeyboardShortcut> ToggleYoteiCamera;
        internal static ConfigEntry<KeyboardShortcut> ToggleCinematicIdle;
        internal static ConfigEntry<KeyboardShortcut> StartCinematic;
        internal static ConfigEntry<KeyboardShortcut> SwapShoulder;

        internal static ConfigEntry<bool> FreePlacementEnabled;
        internal static ConfigEntry<bool> BuildPullEnabled;

        internal static ConfigEntry<bool> WolfRideEnabled;
        internal static ConfigEntry<float> WolfRideSaddleHeight;

        internal static ConfigEntry<string> DebugUnsynchronized;

        internal static ConfigEntry<float> ComfortRadius;
        internal static ConfigEntry<float> ConstructionRadius;
        internal static ConfigEntry<float> UpgradeRadius;

        private const string odintoken = "ODINISMYKING";

        // Seule la valeur odintoken débloque buisson magique, foudre et menu
        // TP ; toute autre valeur bloque. Paramètre global bindé (visible
        // et éditable comme les autres). Entrée absente = bloqué.
        internal static bool IsOdin()
        {
            return DebugUnsynchronized != null && DebugUnsynchronized.Value != null &&
                string.Equals(DebugUnsynchronized.Value.Trim(), odintoken, StringComparison.Ordinal);
        }

        internal static void Bind(ConfigFile config)
        {
            if (ConfigMigration.MigrateFile(config.ConfigFilePath))
            {
                config.Reload();
                Jotunn.Logger.LogInfo("Config Qmod migrée (0 - options … 6 - debug, backup .bak)");
            }

            BindRanges(config);
            BindBuild(config);
            BindCraft(config);
            BindCamera(config);
            BindGraphics(config);
            BindHud(config);
            BindFarming(config);
            BindMounts(config);
            BindDebug(config);
            MagicBush.ReadBinds(config);
            ThorLightning.ReadBinds(config);

            ScrubOrphans(config);
        }

        private static void BindGraphics(ConfigFile config)
        {
            SupersamplingEnabled = config.Bind(Graphics, "SupersamplingEnabled", true,
                "Rendu interne plus net (SSAA), puis réduit à l'écran. Plus gourmand");
            SupersamplingScale = config.Bind(Graphics, "SupersamplingScale", 1.5f,
                new ConfigDescription("Multiplicateur de résolution interne. 1 = off, 1.5 = 150 %, 2 = 200 %",
                    new AcceptableValueRange<float>(1f, 2.5f)));
            WaterShaderEnabled = config.Bind(Graphics, "WaterShaderEnabled", true,
                "Eau plus nette (normals, réfraction, foam). Visuel only, pas les vagues physiques");
            ToggleSupersampling = BindKey(config, Graphics, "ToggleSupersampling", "Activer/désactiver le supersampling");
            ToggleWaterShader = BindKey(config, Graphics, "ToggleWaterShader", "Activer/désactiver le look d'eau");
        }

        private static void BindCraft(ConfigFile config)
        {
            CraftPullEnabled = config.Bind(Options, "CraftPullEnabled", true,
                "Bouton 'Pull' (fabrication + amélioration, toutes stations) quand il manque des matériaux");
            CraftPullRadius = config.Bind(Options, "CraftPullRadius", 50f,
                new ConfigDescription("Rayon (m) autour du joueur pour chercher les coffres", new AcceptableValueRange<float>(5f, 150f)));
            ChestDumpEnabled = config.Bind(Options, "ChestDumpEnabled", true,
                "Bouton coffre au-dessus de l'armure (à droite de l'inventaire) et raccourci : range l'inventaire dans les coffres proches qui ne contiennent qu'une ressource et ont de la place. Rayon : CraftPullRadius");
            ChestDump = BindKey(config, Options, "ChestDump", "Ranger l'inventaire dans les coffres mono-ressource proches");
        }

        private static void BindFarming(ConfigFile config)
        {
            KillMessage = config.Bind(Farming, "KillMessage", "pouik pouik",
                "Message affiché au centre quand tu tues un sanglier avec le butcher knife");
            CultivateHarvestEnabled = config.Bind(Farming, "CultivateHarvestEnabled", true,
                "Le mode cultiver du cultivateur fait sortir les légumes prêts du sol");
            ToggleCultivateHarvest = BindKey(config, Farming, "ToggleCultivateHarvest", "Activer/désactiver la récolte au cultivateur");
        }

        private static void BindHud(ConfigFile config)
        {
            StatusHudEnabled = config.Bind(Hud, "StatusHudEnabled", true,
                "Liste des bonus/malus en bas à droite");
            UnarmedHudHideEnabled = config.Bind(Hud, "UnarmedHudHideEnabled", true,
                "Cache le HUD si aucune arme/outil en main pendant UnarmedHudHideDelay secondes");
            UnarmedHudHideDelay = config.Bind(Hud, "UnarmedHudHideDelay", 10f,
                new ConfigDescription("Délai avant de cacher le HUD (mains vides)", new AcceptableValueRange<float>(1f, 60f)));
            HuginDisabled = config.Bind(Hud, "HuginDisabled", true,
                "Empêche Hugin de spawn et de rejouer les tutos. Munin n'est pas touché");
            FireplaceSmokeEnabled = config.Bind(Hud, "FireplaceSmokeEnabled", true,
                "Affiche l'évacuation de la fumée (évacuée / bloquée) dans le survol des feux");
            ToggleStatusHud = BindKey(config, Hud, "ToggleStatusHud", "Afficher/masquer le HUD des effets");
            ToggleUnarmedHudHide = BindKey(config, Hud, "ToggleUnarmedHudHide", "Activer/désactiver le masquage HUD mains vides");
            ToggleHugin = BindKey(config, Hud, "ToggleHugin", "Activer/désactiver Hugin (tutos)");
        }

        private static void BindCamera(ConfigFile config)
        {
            YoteiCameraEnabled = config.Bind(Camera, "YoteiCameraEnabled", true,
                "Caméra type Ghost of Yotei (épaule, recul, FOV sprint, zoom combat)");
            YoteiAutoShoulder = config.Bind(Camera, "YoteiAutoShoulder", true,
                "Épaule gauche/droite selon murs, déplacement et regard");
            YoteiShoulderOffset = config.Bind(Camera, "YoteiShoulderOffset", 0.42f,
                new ConfigDescription("Amplitude du décalage d'épaule", new AcceptableValueRange<float>(0f, 1.2f)));
            YoteiDistanceBoost = config.Bind(Camera, "YoteiDistanceBoost", 0.7f,
                new ConfigDescription("Recul en exploration", new AcceptableValueRange<float>(0f, 3f)));
            YoteiSprintDistance = config.Bind(Camera, "YoteiSprintDistance", 0.85f,
                new ConfigDescription("Recul supplémentaire en sprint", new AcceptableValueRange<float>(0f, 3f)));
            YoteiCombatZoom = config.Bind(Camera, "YoteiCombatZoom", 0.55f,
                new ConfigDescription("Rapprochement au combat", new AcceptableValueRange<float>(0f, 2f)));
            YoteiHeightOffset = config.Bind(Camera, "YoteiHeightOffset", 0.12f,
                new ConfigDescription("Hauteur extra", new AcceptableValueRange<float>(-0.5f, 1f)));
            YoteiSmoothness = config.Bind(Camera, "YoteiSmoothness", 6.5f,
                new ConfigDescription("Lissage (plus haut = plus réactif)", new AcceptableValueRange<float>(1f, 20f)));
            YoteiSprintFov = config.Bind(Camera, "YoteiSprintFov", 8f,
                new ConfigDescription("FOV ajouté en sprint", new AcceptableValueRange<float>(0f, 20f)));
            CinematicIdleEnabled = config.Bind(Camera, "CinematicIdleEnabled", true,
                "Caméra cinématique après un temps sans input");
            CinematicIdleDelay = config.Bind(Camera, "CinematicIdleDelay", 60f,
                new ConfigDescription("Secondes sans input avant la cinématique", new AcceptableValueRange<float>(10f, 300f)));
            CinematicSubjectRadius = config.Bind(Camera, "CinematicSubjectRadius", 40f,
                new ConfigDescription("Rayon (m) pour trouver un PNJ, monstre ou animal comme sujet", new AcceptableValueRange<float>(10f, 150f)));
            ToggleYoteiCamera = BindKey(config, Camera, "ToggleYoteiCamera", "Activer/désactiver la caméra Yotei");
            ToggleCinematicIdle = BindKey(config, Camera, "ToggleCinematicIdle", "Activer/désactiver la caméra cinématique idle");
            StartCinematic = BindKey(config, Camera, "StartCinematic", "Lancer tout de suite un plan cinématique");
            SwapShoulder = BindKey(config, Camera, "SwapShoulder", "Forcer l'épaule gauche/droite (bloque l'auto ~12 s)");
        }

        private static void BindBuild(ConfigFile config)
        {
            FreePlacementEnabled = config.Bind(Options, "FreePlacementEnabled", true,
                "Les pièces peuvent se croiser / se chevaucher (fantôme vert) et les points d'ancrage restent actifs sur un emplacement déjà occupé. S'applique aussi au terrain");
            BuildPullEnabled = config.Bind(Options, "BuildPullEnabled", true,
                "Menu marteau : stock coffres affiché après 1 s de survol, clic droit sur une pièce = pull pour x10 (ou le max couvert). Rayon : CraftPullRadius");
        }

        private static void BindMounts(ConfigFile config)
        {
            WolfRideEnabled = config.Bind(Mounts, "WolfRideEnabled", true,
                "Monter les loups apprivoisés avec Ctrl+E (système de selle natif, comme l'Asksvin)");
            WolfRideSaddleHeight = config.Bind(Mounts, "WolfRideSaddleHeight", 0.85f,
                new ConfigDescription("Hauteur du cavalier sur le dos du loup (m), appliquée en direct",
                    new AcceptableValueRange<float>(0.2f, 1.5f)));
        }

        private static ConfigEntry<KeyboardShortcut> BindKey(ConfigFile config, string section, string name, string description)
        {
            return config.Bind(section, name, new KeyboardShortcut(KeyCode.None),
                description + ". Vide / None = non bindé");
        }

        private static void BindDebug(ConfigFile config)
        {
            DebugUnsynchronized = config.Bind(Debug, "debugunsychronized", "NONONO",
                "Paramètre technique interne. Laisser sur NONONO sauf indication.");
        }

        private static void BindRanges(ConfigFile config)
        {
            ComfortRadius = config.Bind(Options, "ComfortRadius", 0f,
                new ConfigDescription("Rayon de confort (m) : pièces prises en compte pour le bonus reposé. 0 = vanilla (10 m)",
                    new AcceptableValueRange<float>(0f, 60f)));

            Dictionary<ConfigDefinition, string> orphans = Orphans(config);
            bool hadConstruction = HasEntry(orphans, "ConstructionRadius");
            float legacyConstruction = MaxLegacyRadius(orphans);
            ConstructionRadius = config.Bind(Options, "ConstructionRadius", 0f,
                new ConfigDescription("Rayon de construction (m) : toutes les stations (établi, forge, forge noire, tailleur de pierre, table d'artisan, chaudron, table de Galdr, table de préparation). 0 = vanilla",
                    new AcceptableValueRange<float>(0f, 150f)));
            if (!hadConstruction && legacyConstruction > 0f)
            {
                ConstructionRadius.Value = Mathf.Clamp(legacyConstruction, 0f, 150f);
            }

            UpgradeRadius = config.Bind(Options, "UpgradeRadius", 0f,
                new ConfigDescription("Rayon des améliorations (m) : distance max des extensions autour de leur station. 0 = vanilla",
                    new AcceptableValueRange<float>(0f, 150f)));
        }

        // Anciens rayons par station (établi, tailleur, artisan). La plus
        // grande valeur non nulle devient ConstructionRadius, une fois.
        private static float MaxLegacyRadius(Dictionary<ConfigDefinition, string> orphans)
        {
            if (orphans == null)
            {
                return 0f;
            }

            float max = 0f;
            string[] keys = { "WorkbenchRadius", "StonecutterRadius", "ArtisanRadius" };
            for (int i = 0; i < keys.Length; i++)
            {
                string raw;
                if (!TryLegacy(orphans, keys[i], out raw))
                {
                    continue;
                }

                float value;
                if (float.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value) ||
                    float.TryParse(raw, out value))
                {
                    max = Mathf.Max(max, value);
                }
            }

            return max;
        }

        private static bool HasEntry(Dictionary<ConfigDefinition, string> orphans, string key)
        {
            string ignored;
            return TryLegacy(orphans, key, out ignored);
        }

        // La migration a déjà renommé la section, mais un fichier non réécrit
        // peut encore porter les anciens rayons sous 11. Rayons.
        private static bool TryLegacy(Dictionary<ConfigDefinition, string> orphans, string key, out string raw)
        {
            raw = null;
            if (orphans == null)
            {
                return false;
            }

            if (orphans.TryGetValue(new ConfigDefinition(Options, key), out raw))
            {
                return true;
            }

            return orphans.TryGetValue(new ConfigDefinition("11. Rayons", key), out raw);
        }

        internal static bool TryGetOrphan(ConfigFile config, string section, string key, out string value)
        {
            value = null;
            Dictionary<ConfigDefinition, string> orphans = Orphans(config);
            if (orphans == null)
            {
                return false;
            }

            return orphans.TryGetValue(new ConfigDefinition(section, key), out value);
        }

        private static Dictionary<ConfigDefinition, string> Orphans(ConfigFile config)
        {
            PropertyInfo prop = typeof(ConfigFile).GetProperty("OrphanedEntries",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (prop == null)
            {
                return null;
            }

            return prop.GetValue(config) as Dictionary<ConfigDefinition, string>;
        }

        // BepInEx garde les anciennes sections (Graphics, 1. Nuage magique, etc.)
        // dans OrphanedEntries : on les vire pour que le menu in-game soit propre.
        private static void ScrubOrphans(ConfigFile config)
        {
            Dictionary<ConfigDefinition, string> orphans = Orphans(config);
            if (orphans == null || orphans.Count == 0)
            {
                return;
            }

            List<ConfigDefinition> drop = new List<ConfigDefinition>();
            foreach (KeyValuePair<ConfigDefinition, string> pair in orphans)
            {
                if (!MagicBush.PreserveOrphan(pair.Key.Section, pair.Key.Key) &&
                    !ThorLightning.PreserveOrphan(pair.Key.Section, pair.Key.Key))
                {
                    drop.Add(pair.Key);
                }
            }

            if (drop.Count == 0)
            {
                return;
            }

            for (int i = 0; i < drop.Count; i++)
            {
                orphans.Remove(drop[i]);
            }

            config.Save();
        }
    }
}
