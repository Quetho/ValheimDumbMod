using System;
using System.Collections.Generic;
using HarmonyLib;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace Qmod
{
    // Planche à voile, option A de research/transport : clone d'un bateau
    // vanilla (physique Ship, voile, gouvernail, HUD, réseau : tout hérité),
    // coque masquée, banc vanilla greffé en guise de planche. Posable au
    // marteau sur l'eau comme un radeau. Proto : tuning et visuel suivront
    // les dumps runtime (§5.1 du rapport).
    internal static class Windsurf
    {
        internal const string PrefabName = "QmodWindsurf";
        private const string BoardObjectName = "QmodBoard";
        private static readonly string[] BaseFallbacks = { "Raft", "Karve", "VikingShip" };
        private static readonly string[] BoardFallbacks = { "piece_bench01", "Bench", "piece_bench" };
        private static bool registered;
        // Bras vertical d'origine (Ship.m_sailForceOffset) avant qu'on le coupe.
        // La poussée avant, appliquée au-dessus du centre de masse, plante le nez.
        private static float sailHeelOffset;

        internal static bool IsEnabled =>
            ModConfig.WindsurfEnabled != null && ModConfig.WindsurfEnabled.Value;

        internal static void Register()
        {
            if (registered)
            {
                return;
            }

            registered = true;
            if (!IsEnabled)
            {
                Jotunn.Logger.LogInfo("Windsurf: désactivé (WindsurfEnabled=false)");
                return;
            }

            try
            {
                RegisterInner();
            }
            catch (Exception e)
            {
                Jotunn.Logger.LogWarning("Windsurf: enregistrement impossible : " + e.Message);
            }
        }

        private static void RegisterInner()
        {
            if (PrefabManager.Instance == null || PieceManager.Instance == null)
            {
                Jotunn.Logger.LogWarning("Windsurf: managers Jotunn indisponibles");
                return;
            }

            GameObject basePrefab = FindBasePrefab();
            if (!basePrefab)
            {
                return;
            }

            PieceConfig pieceConfig = new PieceConfig();
            pieceConfig.Name = "Planche à voile";
            pieceConfig.Description = "Un banc, un mât, du vent. Ramez si pétole.";
            pieceConfig.PieceTable = PieceTables.Hammer;
            pieceConfig.Category = PieceCategories.Misc;
            pieceConfig.Usage = new string[] { PieceUsages.Transport };
            pieceConfig.CraftingStation = "piece_workbench";
            pieceConfig.AddRequirement(new RequirementConfig() { Item = "Wood", Amount = 8, Recover = true });
            pieceConfig.AddRequirement(new RequirementConfig() { Item = "Resin", Amount = 4, Recover = true });
            pieceConfig.AddRequirement(new RequirementConfig() { Item = "LeatherScraps", Amount = 2, Recover = true });

            CustomPiece custom;
            try
            {
                custom = new CustomPiece(PrefabName, basePrefab.name, pieceConfig);
            }
            catch (Exception e)
            {
                Jotunn.Logger.LogWarning("Windsurf: clone de " + basePrefab.name + " impossible : " + e.Message);
                return;
            }

            GameObject clone = custom.PiecePrefab;
            if (!clone)
            {
                Jotunn.Logger.LogWarning("Windsurf: prefab cloné vide");
                return;
            }

            DumpHierarchy(basePrefab, "hiérarchie " + basePrefab.name);
            Surgery(clone);
            DumpShipStats(clone);
            DumpColliders(clone);

            if (!PieceManager.Instance.AddPiece(custom))
            {
                Jotunn.Logger.LogWarning("Windsurf: PieceManager a refusé la pièce");
                return;
            }

            Jotunn.Logger.LogInfo("Windsurf: planche enregistrée (base " + basePrefab.name + ")");
        }

        private static GameObject FindBasePrefab()
        {
            List<string> tried = new List<string>();
            string wanted = ModConfig.WindsurfCloneBase != null ? ModConfig.WindsurfCloneBase.Value : null;
            if (!string.IsNullOrWhiteSpace(wanted))
            {
                tried.Add(wanted.Trim());
                GameObject prefab = PrefabManager.Instance.GetPrefab(wanted.Trim());
                if (prefab)
                {
                    return prefab;
                }
            }

            for (int i = 0; i < BaseFallbacks.Length; i++)
            {
                if (tried.Contains(BaseFallbacks[i]))
                {
                    continue;
                }

                tried.Add(BaseFallbacks[i]);
                GameObject prefab = PrefabManager.Instance.GetPrefab(BaseFallbacks[i]);
                if (prefab)
                {
                    Jotunn.Logger.LogInfo("Windsurf: base '" + wanted + "' introuvable, repli sur " + prefab.name);
                    return prefab;
                }
            }

            Jotunn.Logger.LogWarning("Windsurf: bateau vanilla introuvable (essayés : " + string.Join(", ", tried.ToArray()) + ")");
            return null;
        }

        private static void Surgery(GameObject clone)
        {
            Ship ship = clone.GetComponent<Ship>();
            if (!ship)
            {
                Jotunn.Logger.LogWarning("Windsurf: pas de Ship sur le clone, clone nu conservé");
                return;
            }

            GameObject board = AttachBoard(clone);
            SurgeryColliders(clone, ship);
            MoveHelm(clone);
            ResizeBoardTrigger(clone);
            SizeFloatCollider(clone);

            if (ModConfig.WindsurfHideHull != null && ModConfig.WindsurfHideHull.Value)
            {
                HideHull(clone, ship, board);
            }

            TuneSail(ship);
            TuneDynamics(ship);
            TunePivot(ship);
            TuneSteer(ship);
            FlattenSailPitch(ship);

            Piece piece = clone.GetComponent<Piece>();
            if (piece)
            {
                piece.m_waterPiece = true;
            }
        }

        private static GameObject AttachBoard(GameObject clone)
        {
            float scale = ModConfig.WindsurfBoardScale != null
                ? Mathf.Clamp(ModConfig.WindsurfBoardScale.Value, 0.5f, 3f) : 1.6f;
            float height = ModConfig.WindsurfBoardHeight != null
                ? Mathf.Clamp(ModConfig.WindsurfBoardHeight.Value, -1f, 2f) : 0.35f;

            GameObject bench = FindBoardPrefab();
            GameObject board;
            if (bench)
            {
                board = UnityEngine.Object.Instantiate(bench, clone.transform);
                board.name = BoardObjectName;
                StripBoard(board);
                board.transform.localScale = Vector3.one * scale;
                board.AddComponent<WindsurfBoard>();
                FitBoardCollider(board);
                Jotunn.Logger.LogInfo("Windsurf: planche = visuel " + bench.name + " x" + scale);
            }
            else
            {
                board = GameObject.CreatePrimitive(PrimitiveType.Cube);
                board.name = BoardObjectName;
                board.transform.SetParent(clone.transform, false);
                board.transform.localScale = new Vector3(0.9f, 0.12f, 3.4f);
                board.AddComponent<WindsurfBoard>();
                Renderer donor = clone.GetComponentInChildren<MeshRenderer>();
                Renderer own = board.GetComponent<Renderer>();
                if (donor && own && donor.sharedMaterial)
                {
                    own.sharedMaterial = donor.sharedMaterial;
                }

                Jotunn.Logger.LogWarning("Windsurf: banc introuvable, planche = cube de secours");
            }

            CopyHelmLayer(clone, board);
            board.transform.localPosition = new Vector3(0f, height, 0f);
            // Banc dans l'axe de marche (sa longueur est en travers par défaut).
            board.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            return board;
        }

        private static GameObject FindBoardPrefab()
        {
            List<string> tried = new List<string>();
            string wanted = ModConfig.WindsurfBoardPrefab != null ? ModConfig.WindsurfBoardPrefab.Value : null;
            if (!string.IsNullOrWhiteSpace(wanted))
            {
                tried.Add(wanted.Trim());
                GameObject prefab = PrefabManager.Instance.GetPrefab(wanted.Trim());
                if (prefab)
                {
                    return prefab;
                }
            }

            for (int i = 0; i < BoardFallbacks.Length; i++)
            {
                if (tried.Contains(BoardFallbacks[i]))
                {
                    continue;
                }

                tried.Add(BoardFallbacks[i]);
                GameObject prefab = PrefabManager.Instance.GetPrefab(BoardFallbacks[i]);
                if (prefab)
                {
                    return prefab;
                }
            }

            Jotunn.Logger.LogWarning("Windsurf: banc vanilla introuvable (essayés : " + string.Join(", ", tried.ToArray()) + ")");
            return null;
        }

        // Ne garde que le visuel : tout le reste (ZNetView, Piece, colliders,
        // scripts) casserait le bateau hôte une fois parenté au clone.
        private static void StripBoard(GameObject board)
        {
            Component[] parts = board.GetComponentsInChildren<Component>(true);
            for (int i = 0; i < parts.Length; i++)
            {
                Component part = parts[i];
                if (!part || part is Transform || part is MeshFilter || part is MeshRenderer)
                {
                    continue;
                }

                UnityEngine.Object.DestroyImmediate(part);
            }
        }

        // Masque la coque en gardant le gréement (références Ship) et la
        // planche. Seuls les MeshRenderer sont touchés : les particules
        // (sillage, splash) et les sons survivent.
        private static void HideHull(GameObject clone, Ship ship, GameObject board)
        {
            List<string> hidden = new List<string>();
            MeshRenderer[] renderers = clone.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Transform target = renderers[i].transform;
                if (IsUnder(target, board ? board.transform : null))
                {
                    continue;
                }

                if (ship.m_sailObject && IsUnder(target, ship.m_sailObject.transform))
                {
                    continue;
                }

                if (ship.m_mastObject && IsUnder(target, ship.m_mastObject.transform))
                {
                    continue;
                }

                if (ship.m_rudderObject && IsUnder(target, ship.m_rudderObject.transform))
                {
                    continue;
                }

                renderers[i].enabled = false;
                hidden.Add(target.name);
            }

            Jotunn.Logger.LogInfo("Windsurf: coque masquée (" + hidden.Count + " rendus : " + string.Join(", ", hidden.ToArray()) + ")");
        }

        private static bool IsUnder(Transform target, Transform root)
        {
            if (!target || !root)
            {
                return false;
            }

            return target == root || target.IsChildOf(root);
        }

        private static void DumpHierarchy(GameObject root, string title)
        {
            if (!root)
            {
                return;
            }

            try
            {
                List<string> lines = new List<string>();
                DumpChild(root.transform, "", lines, 120);
                Jotunn.Logger.LogInfo("Windsurf: " + title + "\n" + string.Join("\n", lines.ToArray()));
            }
            catch (Exception e)
            {
                Jotunn.Logger.LogWarning("Windsurf: dump hiérarchie impossible : " + e.Message);
            }
        }

        private static void DumpChild(Transform target, string indent, List<string> lines, int cap)
        {
            if (lines.Count >= cap)
            {
                return;
            }

            Component[] parts = target.GetComponents<Component>();
            List<string> names = new List<string>();
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i])
                {
                    names.Add(parts[i].GetType().Name);
                }
            }

            lines.Add(indent + target.name + " [" + string.Join(",", names.ToArray()) + "]");
            for (int i = 0; i < target.childCount && lines.Count < cap; i++)
            {
                DumpChild(target.GetChild(i), indent + "  ", lines, cap);
            }
        }

        // Voile : absolu si réglé, sinon calée sur le drakkar (lu sur le
        // prefab vanilla, pas deviné) multiplié par le facteur config.
        private static void TuneSail(Ship ship)
        {
            float absolute = ModConfig.WindsurfSailForce != null ? ModConfig.WindsurfSailForce.Value : 0f;
            if (absolute > 0f)
            {
                Jotunn.Logger.LogInfo("Windsurf: m_sailForceFactor " + ship.m_sailForceFactor + " -> " + absolute + " (absolu)");
                ship.m_sailForceFactor = absolute;
                return;
            }

            float factor = ModConfig.WindsurfSailFactor != null
                ? Mathf.Clamp(ModConfig.WindsurfSailFactor.Value, 0.5f, 5f) : 1.5f;
            GameObject drakkar = PrefabManager.Instance.GetPrefab("VikingShip");
            Ship drakkarShip = drakkar ? drakkar.GetComponent<Ship>() : null;
            if (drakkarShip)
            {
                float target = drakkarShip.m_sailForceFactor * factor;
                Jotunn.Logger.LogInfo("Windsurf: m_sailForceFactor " + ship.m_sailForceFactor
                    + " -> " + target + " (drakkar " + drakkarShip.m_sailForceFactor + " x " + factor + ")");
                ship.m_sailForceFactor = target;
            }
            else
            {
                Jotunn.Logger.LogWarning("Windsurf: drakkar introuvable, voile radeau conservée (" + ship.m_sailForceFactor + ")");
            }
        }

        // Traînée eau + stabilité copiées sur le drakkar : le damping du
        // radeau plafonnait la VMax, son amortissement angulaire figeait le
        // roulis. Masse et flottabilité gardées (équilibre actuel sain).
        private static void TuneDynamics(Ship ship)
        {
            GameObject drakkar = PrefabManager.Instance.GetPrefab("VikingShip");
            Ship reference = drakkar ? drakkar.GetComponent<Ship>() : null;
            if (!reference)
            {
                Jotunn.Logger.LogWarning("Windsurf: drakkar introuvable, dynamique radeau conservée");
                return;
            }

            float drag = ModConfig.WindsurfDragFactor != null
                ? Mathf.Clamp(ModConfig.WindsurfDragFactor.Value, 0.1f, 2f) : 1f;
            float heel = ModConfig.WindsurfHeelFactor != null
                ? Mathf.Clamp(ModConfig.WindsurfHeelFactor.Value, 0.1f, 3f) : 1f;
            float speed = ModConfig.WindsurfSpeedFactor != null
                ? Mathf.Clamp(ModConfig.WindsurfSpeedFactor.Value, 0.5f, 5f) : 1.5f;

            // Le freinage latéral du drakkar tient une quille. Sur la planche
            // il gomme la vitesse dès que le cap change.
            const float sideSlip = 0.2f;
            Jotunn.Logger.LogInfo("Windsurf: damping " + ship.m_damping + "/" + ship.m_dampingSideway + "/" + ship.m_dampingForward
                + " -> " + (reference.m_damping * drag) + "/" + (reference.m_dampingSideway * drag * sideSlip) + "/" + (reference.m_dampingForward * drag)
                + " (drakkar x " + drag + ", latéral x " + sideSlip + ")");
            ship.m_damping = reference.m_damping * drag;
            ship.m_dampingSideway = reference.m_dampingSideway * drag * sideSlip;
            ship.m_dampingForward = reference.m_dampingForward * drag;

            Jotunn.Logger.LogInfo("Windsurf: angularDamping " + ship.m_angularDamping
                + " -> " + (reference.m_angularDamping * heel) + " (drakkar x " + heel + ")");
            ship.m_angularDamping = reference.m_angularDamping * heel;

            Rigidbody body = ship.GetComponent<Rigidbody>();
            Rigidbody bodyRef = drakkar.GetComponent<Rigidbody>();
            if (body && bodyRef)
            {
                const float maxSpin = 1.6f;
                Jotunn.Logger.LogInfo("Windsurf: rigidbody linearDamping " + body.linearDamping + "->" + bodyRef.linearDamping
                    + " angularDamping " + body.angularDamping + "->" + bodyRef.angularDamping
                    + " maxLinear " + body.maxLinearVelocity + "->" + (bodyRef.maxLinearVelocity * speed)
                    + " maxAngular " + body.maxAngularVelocity + "->" + maxSpin
                    + " (masse lue " + body.mass + ", drakkar " + bodyRef.mass + ")");
                body.linearDamping = bodyRef.linearDamping;
                body.angularDamping = bodyRef.angularDamping;
                body.maxLinearVelocity = bodyRef.maxLinearVelocity * speed;
                body.maxAngularVelocity = maxSpin;
            }
        }

        // La barre pousse sur le côté en (origine + avant * offset). Le centre
        // de masse est sous le rider : si l'offset du radeau tombe au même
        // endroit, le couple de lacet est nul et la planche file droit.
        private static void TuneSteer(Ship ship)
        {
            GameObject raftObject = PrefabManager.Instance != null
                ? PrefabManager.Instance.GetPrefab("Raft") : null;
            Ship raft = raftObject ? raftObject.GetComponent<Ship>() : null;
            if (!raft)
            {
                raft = ship;
            }

            float pivotZ = ModConfig.WindsurfPivotZ != null
                ? Mathf.Clamp(ModConfig.WindsurfPivotZ.Value, -1.5f, 0.5f) : -0.5f;
            const float lever = 2.2f;
            const float gain = 2.5f;
            float offset = pivotZ - lever;
            Jotunn.Logger.LogInfo("Windsurf: barre offset " + ship.m_stearForceOffset + " -> " + offset
                + " (bras " + lever + " m derrière le centre de masse)"
                + " vel " + raft.m_stearVelForceFactor + " -> " + (raft.m_stearVelForceFactor * gain)
                + " rame " + raft.m_stearForce + " -> " + (raft.m_stearForce * gain));
            ship.m_stearForceOffset = offset;
            ship.m_stearVelForceFactor = raft.m_stearVelForceFactor * gain;
            ship.m_stearForce = raft.m_stearForce * gain;
            ship.m_rudderSpeed = raft.m_rudderSpeed * 3f;
        }

        // Dimensions de la planche exprimées dans l'espace du bateau.
        // La planche est pivotée à 90° : x/z échangés.
        private static Vector3 BoardShipSize(Transform board, BoxCollider box)
        {
            Vector3 local = board.localScale;
            return new Vector3(
                box.size.z * Mathf.Max(local.z, 0.01f),
                box.size.y * Mathf.Max(local.y, 0.01f),
                box.size.x * Mathf.Max(local.x, 0.01f));
        }

        // Pivot sous le rider (lacet autour des pieds, pas du milieu comme
        // un gros bateau) + assiette légèrement cabrée. Inertie volontairement
        // gardée auto : le roulis actuel est validé, on n'y touche pas.
        private static void TunePivot(Ship ship)
        {
            Rigidbody body = ship.GetComponent<Rigidbody>();
            if (!body)
            {
                return;
            }

            float balanceY = ModConfig.WindsurfBalanceY != null
                ? Mathf.Clamp(ModConfig.WindsurfBalanceY.Value, -1f, 0.5f) : -0.1f;
            float pivotZ = ModConfig.WindsurfPivotZ != null
                ? Mathf.Clamp(ModConfig.WindsurfPivotZ.Value, -1.5f, 0.5f) : -0.5f;
            body.automaticCenterOfMass = false;
            body.centerOfMass = new Vector3(0f, balanceY, pivotZ);
            Jotunn.Logger.LogInfo("Windsurf: centre de masse -> " + body.centerOfMass + " (pivot sous rider)");
        }

        // La voile vanilla pousse en (vent + avant) au-dessus du centre de masse :
        // la part vers l'avant crée un couple de tangage (nez qui plante). On
        // applique toute la poussée au centre, et on ne relève que la part
        // latérale pour garder la gîte.
        private static void FlattenSailPitch(Ship ship)
        {
            if (!ship)
            {
                return;
            }

            if (Mathf.Abs(sailHeelOffset) < 0.001f && Mathf.Abs(ship.m_sailForceOffset) > 0.001f)
            {
                sailHeelOffset = ship.m_sailForceOffset;
                Jotunn.Logger.LogInfo("Windsurf: gîte latérale bras " + sailHeelOffset
                    + " (tangage de la voile annulé)");
            }

            ship.m_sailForceOffset = 0f;
        }

        // Le bateau échantillonne 5 points : centre + bords du FloatCollider.
        // La largeur (gîte) reste celle du radeau. La longueur colle au banc :
        // assez pour porter le nez, sans le mètre d'allonge qui suivait chaque vague.
        private const float PitchSpan = 1f;

        private static void SizeFloatCollider(GameObject root)
        {
            Transform board = root.transform.Find(BoardObjectName);
            BoxCollider box = board ? board.GetComponent<BoxCollider>() : null;
            Transform floater = root.transform.Find("FloatCollider");
            BoxCollider water = floater ? floater.GetComponent<BoxCollider>() : null;
            if (!box || !water)
            {
                Jotunn.Logger.LogWarning("Windsurf: FloatCollider ou hitbox introuvable");
                return;
            }

            Vector3 boardSize = BoardShipSize(board, box);
            Vector3 oldSize = water.size;
            Vector3 oldCenter = water.center;
            float length = Mathf.Max(boardSize.z * PitchSpan, 0.8f);
            water.size = new Vector3(oldSize.x, oldSize.y, length);
            water.center = new Vector3(oldCenter.x, oldCenter.y, 0f);
            Jotunn.Logger.LogInfo("Windsurf: FloatCollider taille " + oldSize + " -> " + water.size
                + " centre " + oldCenter + " -> " + water.center + " (planche " + boardSize + ")");
        }

        private static void DumpColliders(GameObject clone)
        {
            try
            {
                Collider[] cols = clone.GetComponentsInChildren<Collider>(true);
                List<string> lines = new List<string>();
                for (int i = 0; i < cols.Length && lines.Count < 40; i++)
                {
                    Collider col = cols[i];
                    if (!col)
                    {
                        continue;
                    }

                    string path = col.transform.name;
                    Transform parent = col.transform.parent;
                    while (parent && parent != clone.transform)
                    {
                        path = parent.name + "/" + path;
                        parent = parent.parent;
                    }

                    lines.Add(path + " [" + col.GetType().Name
                        + " trigger=" + col.isTrigger + " layer=" + col.gameObject.layer
                        + " on=" + (col.enabled && col.gameObject.activeInHierarchy) + "]");
                }

                Jotunn.Logger.LogInfo("Windsurf: colliders (" + cols.Length + ")\n" + string.Join("\n", lines.ToArray()));
            }
            catch (Exception e)
            {
                Jotunn.Logger.LogWarning("Windsurf: dump colliders impossible : " + e.Message);
            }
        }

        private static void DumpShipStats(GameObject clone)
        {
            try
            {
                Ship ship = clone.GetComponent<Ship>();
                if (!ship)
                {
                    return;
                }

                ShipControlls controls = clone.GetComponentInChildren<ShipControlls>(true);
                string anim = controls ? controls.m_attachAnimation : "<sans ShipControlls>";
                float range = controls ? controls.m_maxUseRange : -1f;
                Jotunn.Logger.LogInfo("Windsurf: Ship sail=" + ship.m_sailForceFactor
                    + " force=" + ship.m_force + " rudderSpeed=" + ship.m_rudderSpeed
                    + " stear=" + ship.m_stearForce + " attach='" + anim + "' range=" + range);
            }
            catch (Exception e)
            {
                Jotunn.Logger.LogWarning("Windsurf: dump Ship impossible : " + e.Message);
            }
        }

        // Tue les hitboxes solides de la coque (Cube + rondins, devenues des
        // murs invisibles), l'échelle et le siège passager (montée Ctrl+E
        // + rider debout les remplacent). Gardés : OnboardTrigger,
        // FloatCollider, helm, mât, effets.
        private static void SurgeryColliders(GameObject clone, Ship ship)
        {
            int killed = 0;
            Transform hull = clone.transform.Find("ship/colliders");
            if (hull)
            {
                Collider[] cols = hull.GetComponentsInChildren<Collider>(true);
                for (int i = 0; i < cols.Length; i++)
                {
                    if (cols[i])
                    {
                        UnityEngine.Object.DestroyImmediate(cols[i]);
                        killed++;
                    }
                }
            }

            bool ladder = false;
            Transform ladderObject = clone.transform.Find("interactive/ladder");
            if (ladderObject)
            {
                UnityEngine.Object.DestroyImmediate(ladderObject.gameObject);
                ladder = true;
            }

            bool chair = false;
            Transform chairObject = clone.transform.Find("interactive/mast");
            if (chairObject && !IsRigObject(ship, chairObject.gameObject))
            {
                UnityEngine.Object.DestroyImmediate(chairObject.gameObject);
                chair = true;
            }

            Jotunn.Logger.LogInfo("Windsurf: colliders coque tués=" + killed
                + " échelle retirée=" + ladder + " siège retiré=" + chair);
        }

        private static bool IsRigObject(Ship ship, GameObject go)
        {
            if (ship == null || go == null)
            {
                return false;
            }

            return go == ship.m_sailObject || go == ship.m_mastObject || go == ship.m_rudderObject;
        }

        // Helm recentré sur la planche (il est à l'arrière du radeau, donc
        // dans le vide une fois la coque partie) + portée E élargie.
        private static void MoveHelm(GameObject clone)
        {
            ShipControlls controls = clone.GetComponentInChildren<ShipControlls>(true);
            if (!controls)
            {
                Jotunn.Logger.LogWarning("Windsurf: helm introuvable");
                return;
            }

            float height = ModConfig.WindsurfBoardHeight != null
                ? Mathf.Clamp(ModConfig.WindsurfBoardHeight.Value, -1f, 2f) : 0.35f;
            Vector3 old = controls.transform.localPosition;
            controls.transform.localPosition = new Vector3(0f, height + 0.1f, 0f);
            controls.transform.localRotation = Quaternion.identity;
            controls.m_maxUseRange = 4f;
            Collider[] helmCols = controls.GetComponents<Collider>();
            for (int i = 0; i < helmCols.Length; i++)
            {
                if (helmCols[i])
                {
                    helmCols[i].isTrigger = true;
                }
            }

            Jotunn.Logger.LogInfo("Windsurf: helm " + old + " -> " + controls.transform.localPosition + ", portée E=4, helm non-bloquant");
        }

        // Le trigger de pont (qui embarque le rider avec le bateau) est
        // retaillé sur la planche : l'ancien couvrait le pont du radeau.
        private static void ResizeBoardTrigger(GameObject clone)
        {
            Transform triggerObject = clone.transform.Find("OnboardTrigger");
            if (!triggerObject)
            {
                Jotunn.Logger.LogWarning("Windsurf: OnboardTrigger introuvable");
                return;
            }

            float height = ModConfig.WindsurfBoardHeight != null
                ? Mathf.Clamp(ModConfig.WindsurfBoardHeight.Value, -1f, 2f) : 0.35f;
            BoxCollider box = triggerObject.GetComponent<BoxCollider>();
            string old = triggerObject.localPosition + " / " + (box ? box.size.ToString() : "<sans box>");
            triggerObject.localPosition = new Vector3(0f, height + 1f, 0f);
            triggerObject.localRotation = Quaternion.identity;
            triggerObject.localScale = Vector3.one;
            if (box)
            {
                box.center = Vector3.zero;
                box.size = new Vector3(4f, 3f, 6f);
                box.isTrigger = true;
            }

            Jotunn.Logger.LogInfo("Windsurf: OnboardTrigger " + old + " -> "
                + triggerObject.localPosition + " / " + (box ? box.size.ToString() : "<sans box>"));
        }

        // Hitbox = taille du banc : un BoxCollider ajusté sur les meshes.
        private static void FitBoardCollider(GameObject board)
        {
            Renderer[] renderers = board.GetComponentsInChildren<Renderer>(true);
            Bounds bounds = new Bounds();
            bool found = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                if (!renderers[i])
                {
                    continue;
                }

                if (!found)
                {
                    bounds = renderers[i].bounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(renderers[i].bounds);
                }
            }

            BoxCollider box = board.AddComponent<BoxCollider>();
            box.isTrigger = false;
            box.enabled = true;
            float lossy = board.transform.lossyScale.x;
            if (found && bounds.size.sqrMagnitude > 0.0001f && lossy > 0.01f)
            {
                box.center = board.transform.InverseTransformPoint(bounds.center);
                box.size = bounds.size / lossy;
                Jotunn.Logger.LogInfo("Windsurf: hitbox banc centre=" + box.center + " taille=" + box.size);
            }
            else
            {
                box.center = Vector3.zero;
                box.size = new Vector3(1f, 0.4f, 3f);
                Jotunn.Logger.LogWarning("Windsurf: bornes du banc illisibles, hitbox par défaut");
            }
        }

        // Même layer que la coque (qui bloquait le joueur : collision
        // prouvée) sur la planche, sinon repli sur le helm. Tourne avant
        // SurgeryColliders, quand la coque existe encore.
        private static void CopyHelmLayer(GameObject clone, GameObject board)
        {
            int layer = 0;
            string source = "défaut";
            Transform hull = clone.transform.Find("ship/colliders");
            Collider proven = hull ? hull.GetComponentInChildren<Collider>(true) : null;
            if (proven)
            {
                layer = proven.gameObject.layer;
                source = "coque";
            }
            else
            {
                ShipControlls controls = clone.GetComponentInChildren<ShipControlls>(true);
                Collider helm = controls ? controls.GetComponent<Collider>() : null;
                if (helm)
                {
                    layer = helm.gameObject.layer;
                    source = "helm";
                }
            }

            Transform[] all = board.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i])
                {
                    all[i].gameObject.layer = layer;
                }
            }

            Jotunn.Logger.LogInfo("Windsurf: planche layer=" + layer + " (" + source + ")");
        }

        internal static bool IsWindsurfShip(Ship ship)
        {
            return ship && ship.transform.Find(BoardObjectName);
        }

        internal static ShipControlls FindWindsurfControls(GameObject go)
        {
            if (go == null)
            {
                return null;
            }

            Ship ship = go.GetComponentInParent<Ship>();
            if (!IsWindsurfShip(ship))
            {
                return null;
            }

            return ship.GetComponentInChildren<ShipControlls>();
        }

        internal static bool IsControlling(Player player, ShipControlls controls)
        {
            return player && controls &&
                ReferenceEquals(player.GetDoodadController(), controls);
        }

        private static bool IsCtrlDown()
        {
            return Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        }

        private static void Mount(Player player, ShipControlls controls)
        {
            if (player.IsAttached())
            {
                player.AttachStop();
            }

            Ship ship = controls.GetComponentInParent<Ship>();
            Vector3 target = controls.transform.position + Vector3.up * 1f;
            if (ship)
            {
                Transform board = ship.transform.Find(BoardObjectName);
                BoxCollider box = board ? board.GetComponent<BoxCollider>() : null;
                if (board && box)
                {
                    Vector3 topLocal = box.center + Vector3.up * (box.size.y * 0.5f);
                    target = board.transform.TransformPoint(topLocal) + Vector3.up * 0.7f;
                    // Rider derrière le mât, dans l'axe (stance planche).
                    Vector3 aft = -ship.transform.forward;
                    aft.y = 0f;
                    if (aft.sqrMagnitude > 0.0001f)
                    {
                        target += aft.normalized * 0.9f;
                    }
                }
            }

            player.transform.position = target;
            Rigidbody body = player.m_body;
            if (body)
            {
                body.position = target;
                body.linearVelocity = Vector3.zero;
            }

            controls.Interact(player, false, false);
            Jotunn.Logger.LogInfo("Windsurf: montée demandée");
        }

        // Re-applique les réglages numériques (voile, drag, stabilité,
        // VMax) aux planches déjà posées + au prefab (prochaines poses).
        // La structure (banc, hitbox, helm) reste au lancement.
        internal static void RefreshLiveTuning()
        {
            if (!IsEnabled)
            {
                return;
            }

            try
            {
                if (PrefabManager.Instance == null)
                {
                    return;
                }

                GameObject drakkar = PrefabManager.Instance.GetPrefab("VikingShip");
                if (!drakkar || !drakkar.GetComponent<Ship>())
                {
                    return;
                }

                GameObject prefab = PrefabManager.Instance.GetPrefab(PrefabName);
                Ship prefabShip = prefab ? prefab.GetComponent<Ship>() : null;
                if (prefabShip)
                {
                    TuneSail(prefabShip);
                    TuneDynamics(prefabShip);
                    TunePivot(prefabShip);
                    TuneSteer(prefabShip);
                    FlattenSailPitch(prefabShip);
                }

                int count = 0;
                Ship[] ships = UnityEngine.Object.FindObjectsByType<Ship>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                for (int i = 0; i < ships.Length; i++)
                {
                    Ship ship = ships[i];
                    if (!IsWindsurfShip(ship))
                    {
                        continue;
                    }

                    TuneSail(ship);
                    TuneDynamics(ship);
                    TunePivot(ship);
                    TuneSteer(ship);
                    FlattenSailPitch(ship);
                    count++;
                }

                Jotunn.Logger.LogInfo("Windsurf: tuning à chaud appliqué (" + count + " planche(s) en scène)");
            }
            catch (Exception e)
            {
                Jotunn.Logger.LogWarning("Windsurf: tuning à chaud impossible : " + e.Message);
            }
        }

        internal static string MountHint(Player player, ShipControlls controls)
        {
            string line = IsControlling(player, controls) ? "Descendre" : "Monter";
            string raw = "\n[<color=yellow><b>Ctrl + $KEY_Use</b></color>] " + line;
            return Localization.instance != null ? Localization.instance.Localize(raw) : raw;
        }

        [HarmonyPatch(typeof(Player), nameof(Player.Interact))]
        private static class InteractPatch
        {
            private static bool Prefix(Player __instance, GameObject go, bool hold, bool alt)
            {
                if (!IsEnabled || __instance == null || __instance != Player.m_localPlayer)
                {
                    return true;
                }

                if (hold || alt || !IsCtrlDown() || MagicBush.IsActive)
                {
                    return true;
                }

                ShipControlls controls = FindWindsurfControls(go);
                if (controls == null)
                {
                    return true;
                }

                IDoodadController current = __instance.GetDoodadController();
                if (current != null && !ReferenceEquals(current, controls))
                {
                    return true;
                }

                if (current != null)
                {
                    __instance.StopDoodadControl();
                    Jotunn.Logger.LogInfo("Windsurf: descente");
                }
                else
                {
                    Mount(__instance, controls);
                }

                return false;
            }
        }

        [HarmonyPatch(typeof(ShipControlls), nameof(ShipControlls.GetHoverText))]
        private static class HelmHoverPatch
        {
            private static void Postfix(ShipControlls __instance, ref string __result)
            {
                if (!IsEnabled || __instance == null)
                {
                    return;
                }

                if (!IsWindsurfShip(__instance.GetComponentInParent<Ship>()))
                {
                    return;
                }

                __result += MountHint(Player.m_localPlayer, __instance);
            }
        }

        // Le drakkar amortit le lacet plus vite que la barre ne le crée.
        // On réécrit la vitesse de rotation autour de la verticale : même
        // sens que la poussée vanilla (poupe vers -right si la barre est > 0).
        private static void ApplyYaw(Ship ship, Rigidbody body)
        {
            float rudder = ship.m_rudderValue;
            if (Mathf.Abs(rudder) < 0.02f)
            {
                return;
            }

            Vector3 torque = Vector3.Cross(-ship.transform.forward, ship.transform.right * -Mathf.Sign(rudder));
            float align = Vector3.Dot(torque, Vector3.up);
            if (Mathf.Abs(align) < 0.001f)
            {
                return;
            }

            float speed = body.linearVelocity.magnitude;
            // Entre « presque pas » et le 1.1–2 rad/s trop vif : ~90° en 3 s
            // à l'arrêt, un peu moins de 2 s une fois lancé.
            float rate = Mathf.Lerp(0.55f, 1f, Mathf.Clamp01(speed / 8f));
            float target = Mathf.Sign(align) * rate * Mathf.Abs(rudder);
            Vector3 angular = body.angularVelocity;
            float yaw = Vector3.Dot(angular, Vector3.up);
            body.angularVelocity = angular + Vector3.up * (target - yaw);
        }

        // Couple pur : +latéral en hauteur, -latéral au centre. La poussée
        // linéaire reste celle du jeu (au centre de masse, sans tangage).
        private static void ApplyHeel(Ship ship, Rigidbody body)
        {
            if (Mathf.Abs(sailHeelOffset) < 0.001f)
            {
                return;
            }

            Vector3 sail = ship.m_sailForce;
            Vector3 heel = ship.transform.right * Vector3.Dot(sail, ship.transform.right);
            if (heel.sqrMagnitude < 0.000001f)
            {
                return;
            }

            float rollDeg = Mathf.Abs(Mathf.Asin(Mathf.Clamp(ship.transform.right.y, -1f, 1f))) * Mathf.Rad2Deg;
            float scale = Mathf.InverseLerp(MaxRollDeg, MaxRollDeg * 0.55f, rollDeg);
            if (scale <= 0.02f)
            {
                return;
            }

            Vector3 accel = heel * body.mass * scale;
            Vector3 com = body.worldCenterOfMass;
            Vector3 raised = com + ship.transform.up * sailHeelOffset;
            body.AddForceAtPosition(accel, raised, ForceMode.Acceleration);
            body.AddForceAtPosition(-accel, com, ForceMode.Acceleration);
        }

        // Butées. Le couple de tangage précédent amplifiait l'angle au lieu
        // de le freiner. Ici on ne pousse rien : passé la limite, on rapproche
        // la rotation de l'assiette à plat (même cap), et on plafonne la
        // vitesse de tangage/gîte pour ne pas traverser la butée d'un coup.
        private const float MaxTiltRate = 0.6f;

        private static float MaxPitchDeg
        {
            get
            {
                return ModConfig.WindsurfMaxPitch != null
                    ? Mathf.Clamp(ModConfig.WindsurfMaxPitch.Value, 5f, 80f) : 18f;
            }
        }

        private static float MaxRollDeg
        {
            get
            {
                return ModConfig.WindsurfMaxRoll != null
                    ? Mathf.Clamp(ModConfig.WindsurfMaxRoll.Value, 5f, 80f) : 35f;
            }
        }

        private static void ClampAttitude(Ship ship, Rigidbody body)
        {
            Quaternion current = body.rotation;
            Vector3 forward = current * Vector3.forward;
            Vector3 right = current * Vector3.right;
            Vector3 up = current * Vector3.up;
            float pitch = Mathf.Asin(Mathf.Clamp(forward.y, -1f, 1f));
            float roll = Mathf.Asin(Mathf.Clamp(right.y, -1f, 1f));
            float pitchExcess = Mathf.Abs(pitch) - MaxPitchDeg * Mathf.Deg2Rad;
            float rollExcess = Mathf.Abs(roll) - MaxRollDeg * Mathf.Deg2Rad;
            bool past = pitchExcess > 0.008f || rollExcess > 0.008f || up.y < 0.25f;

            Vector3 angular = body.angularVelocity;
            Vector3 yawSpin = Vector3.Project(angular, Vector3.up);
            Vector3 tilt = angular - yawSpin;

            if (past)
            {
                Vector3 flat = Vector3.ProjectOnPlane(forward, Vector3.up);
                if (flat.sqrMagnitude < 0.04f)
                {
                    flat = Vector3.ProjectOnPlane(up, Vector3.up);
                }

                if (flat.sqrMagnitude < 0.0001f)
                {
                    flat = Vector3.forward;
                }

                Quaternion upright = Quaternion.LookRotation(flat.normalized, Vector3.up);
                float total = Quaternion.Angle(current, upright);
                float excessDeg = Mathf.Max(pitchExcess, rollExcess) * Mathf.Rad2Deg;
                if (up.y < 0.25f)
                {
                    excessDeg = Mathf.Max(excessDeg, 35f);
                }

                float step = total < 0.5f ? 1f : Mathf.Clamp01((excessDeg + 1.5f) / total);
                step = Mathf.Min(step, 22f / Mathf.Max(total, 0.5f));
                body.MoveRotation(Quaternion.Slerp(current, upright, step));
                tilt = Vector3.zero;
            }
            else
            {
                float tiltMag = tilt.magnitude;
                if (tiltMag > MaxTiltRate)
                {
                    tilt *= MaxTiltRate / tiltMag;
                }
            }

            body.angularVelocity = yawSpin + tilt;
        }

        [HarmonyPatch(typeof(Ship), nameof(Ship.CustomFixedUpdate))]
        private static class SailHeelPatch
        {
            private static void Postfix(Ship __instance)
            {
                if (!IsEnabled || !IsWindsurfShip(__instance))
                {
                    return;
                }

                ZNetView view = __instance.m_nview;
                if (view && !view.IsOwner())
                {
                    return;
                }

                Rigidbody body = __instance.m_body;
                if (!body)
                {
                    return;
                }

                ApplyYaw(__instance, body);
                ApplyHeel(__instance, body);
                ClampAttitude(__instance, body);
            }
        }
    }

    // Visée de la planche : Piece n'est pas Hoverable, donc viser le banc
    // ne résolvait rien. Ce composant affiche le hint Ctrl+E ; E simple
    // reste le helm vanilla (transféré tel quel).
    internal class WindsurfBoard : MonoBehaviour, Hoverable, Interactable
    {
        private ShipControlls controls;

        private void Awake()
        {
            Ship ship = GetComponentInParent<Ship>();
            controls = ship ? ship.GetComponentInChildren<ShipControlls>() : null;
        }

        public string GetHoverName()
        {
            return "Planche à voile";
        }

        public string GetHoverText()
        {
            if (!Windsurf.IsEnabled)
            {
                return "";
            }

            if (controls == null)
            {
                Ship ship = GetComponentInParent<Ship>();
                controls = ship ? ship.GetComponentInChildren<ShipControlls>() : null;
            }

            return Windsurf.MountHint(Player.m_localPlayer, controls);
        }

        public float GetHoverOffset()
        {
            return 0.5f;
        }

        public bool Interact(Humanoid character, bool repeat, bool alt)
        {
            if (controls == null)
            {
                return false;
            }

            return controls.Interact(character, repeat, alt);
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item)
        {
            return false;
        }
    }
}
