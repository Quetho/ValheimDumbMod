using HarmonyLib;
using UnityEngine;

namespace Qmod
{
    // Mode Seidr : caméra à contextes, chaque contexte définit son cadrage
    // et sa dynamique, les poids se mélangent en continu :
    //   Explore   : base over-shoulder, vive et respirante.
    //   Sprint    : recul + FOV + rebond, d'autant plus fougueux qu'il y a de la place.
    //   Combat    : recentrée, zoom avant, FOV élargi pour lire les ennemis.
    //   Action    : punch bref sur roulade/saut + secousse trauma.
    //   Mystique  : éloignée, dérive lente, souffle, profondeur de champ.
    //   Intérieur : abri/donjon/confiné = proche, basse, resserrée, tout ralenti.
    // Config 100 % Seidr (Seidr*) : aucun réglage Yotei partagé.
    // Code volontairement séparé de YoteiCamera / AutoShoulderCamera.
    internal static class SeidrCamera
    {
        // Poids des contextes.
        private static float sprintBlend;
        private static float combatBlend;
        private static float mysticBlend;
        private static float actionBlend;
        private static float confineBlend;
        private static float shelterBlend;
        private static float dungeonBlend;

        // Cadrage lissé (par axe : latéral vif, bras moyen, vertical doux).
        private static Vector3 extraOffset;
        private static float smLat;
        private static float smVer;
        private static float smBoom;
        private static float velLat;
        private static float velVer;
        private static float velBoom;

        // Épaule auto.
        private static float shoulderSign = 1f;
        private static float shoulderVel;
        private static float targetSign = 1f;
        private static float lastSwapTime = -10f;
        private static float autoResumeAt;
        private static float shoulderProbeTimer;
        private static float pendingSide;
        private static float pendingTime;

        // Cadence des sondes d'épaule : c'est aussi le pas d'accumulation
        // de pendingTime (la confirmation 0.5 s = 2 pas). Ne pas changer
        // l'un sans l'autre.
        private const float ShoulderProbeStep = 0.25f;

        // Vitesse du fondu FOV à la pause Seidr (sprint +8 ≈ 0.27 s).
        private const float FovFadeRate = 30f;

        private const float CombatFovAdd = 2.5f;
        private const float ActionFovAdd = 3f;

        // Environnement.
        private static float allowPull = 4f;
        private static float allowShoulder = 1.2f;
        private static float sprintRoomSm = 1f;
        private static float ceilingClear = 2.5f;
        private static float ceilingFar = 2.5f;
        private static bool ceilingBlocked;
        private static float envProbeTimer;
        private static float frontClear = 4f;
        private static float rearClear = 4f;
        private static float rearFar = 8f;
        private static float rightClear = 3f;
        private static float leftClear = 3f;
        private static bool dungeonRaw;
        private static float dungeonProbeTimer;

        // Sorties rotation / FOV / trauma.
        private static float leadLat;
        private static float leadFwd;
        private static float fovTarget;
        private static float pitchDrift;
        private static bool appliedFov;
        private static float trauma;
        private static float prevVy;
        private static bool wasDodging;
        private static float idleTime;
        private static readonly CameraDof mysticDof = new CameraDof();

        private static bool ShouldSkip(GameCamera camera, Player player)
        {
            return CameraUtil.ShouldSkip(ShoulderCameraMode.Seidr, camera, player);
        }

        private static void ResetState()
        {
            sprintBlend = 0f;
            combatBlend = 0f;
            mysticBlend = 0f;
            actionBlend = 0f;
            confineBlend = 0f;
            shelterBlend = 0f;
            dungeonBlend = 0f;
            extraOffset = Vector3.zero;
            smLat = 0f;
            smVer = 0f;
            smBoom = 0f;
            velLat = 0f;
            velVer = 0f;
            velBoom = 0f;
            shoulderVel = 0f;
            pendingSide = 0f;
            pendingTime = 0f;
            allowPull = 4f;
            allowShoulder = 1.2f;
            sprintRoomSm = 1f;
            ceilingClear = 2.5f;
            ceilingFar = 2.5f;
            ceilingBlocked = false;
            frontClear = 4f;
            rearClear = 4f;
            rearFar = 8f;
            rightClear = 3f;
            leftClear = 3f;
            dungeonRaw = false;
            dungeonProbeTimer = 0f;
            leadLat = 0f;
            leadFwd = 0f;
            pitchDrift = 0f;
            trauma = 0f;
            prevVy = 0f;
            wasDodging = false;
            idleTime = 0f;
        }

        // Fondu de sortie : quand Seidr se met en pause (arc, lit, build,
        // cinématique, changement de mode...), on dissipe l'offset et les
        // blends en ~0.3 s au lieu de couper net (pop). Reset complet une
        // fois dissipé. L'épaule choisie est conservée dans les deux cas.
        private static void FadeState(float dt)
        {
            float k = CameraUtil.FadeExp(dt);
            sprintBlend *= k;
            combatBlend *= k;
            mysticBlend *= k;
            actionBlend *= k;
            confineBlend *= k;
            shelterBlend *= k;
            dungeonBlend *= k;
            trauma = Mathf.Max(0f, trauma - dt * 1.8f);
            smLat *= k;
            smVer *= k;
            smBoom *= k;
            velLat *= k;
            velVer *= k;
            velBoom *= k;
            shoulderVel *= k;
            leadLat *= k;
            leadFwd *= k;
            pitchDrift *= k;
            extraOffset *= k;
            if (extraOffset.sqrMagnitude < 0.000001f
                && sprintBlend < 0.01f && combatBlend < 0.01f && mysticBlend < 0.01f
                && actionBlend < 0.01f && confineBlend < 0.01f && shelterBlend < 0.01f && dungeonBlend < 0.01f)
            {
                ResetState();
            }
        }

        [HarmonyPatch(typeof(GameCamera), "GetCameraOffset")]
        private static class GetCameraOffsetPatch
        {
            private static void Postfix(GameCamera __instance, Player player, ref Vector3 __result)
            {
                if (ShouldSkip(__instance, player))
                {
                    FadeState(Time.deltaTime);
                    if (ModConfig.CameraMode != null && ModConfig.CameraMode.Value == ShoulderCameraMode.Seidr
                        && !CinematicIdleCamera.IsActive)
                    {
                        __result += extraOffset;
                    }

                    return;
                }

                float dt = Time.deltaTime;
                float now = Time.time;
                float sway = ModConfig.SeidrSway.Value;

                Transform eye = player.m_eye ? player.m_eye : player.transform;
                Vector3 right = eye.right;
                Vector3 up = Vector3.up;
                Vector3 back = -eye.forward;
                Vector3 origin = eye.position;

                Vector3 velocity = player.GetVelocity();
                Vector3 flatVel = velocity;
                flatVel.y = 0f;
                float hSpeed = flatVel.magnitude;
                float forwardSpeed = Vector3.Dot(velocity, eye.forward);
                bool riding = MagicBush.IsRiding;

                // ---- 1. Poids des contextes ----
                float sprintTarget = (player.IsRunning() && !player.IsSneaking()) || MagicBush.IsThrusting ? 1f : 0f;
                float combatTarget = player.UseMeleeCamera() || player.InAttack() || player.IsBlocking() ? 1f : 0f;
                if (riding)
                {
                    combatTarget = 0f;
                }
                bool dodging = player.InDodge();
                float actionTarget = dodging || velocity.y > 3.5f ? 1f : 0f;

                sprintBlend = DampAsymmetric(sprintBlend, sprintTarget, dt, 3.5f, 1.8f);
                combatBlend = DampAsymmetric(combatBlend, combatTarget, dt, 4.5f, 1.8f);
                actionBlend = DampAsymmetric(actionBlend, actionTarget, dt, 6f, 2.5f);

                if (CameraUtil.HasPlayerInput() || Util.IsMenuBlocking() || !Util.IsAlive(player))
                {
                    idleTime = 0f;
                }
                else
                {
                    idleTime += dt;
                }
                float mysticTarget = idleTime >= ModConfig.SeidrIdleDelay.Value && sprintTarget <= 0f && combatTarget <= 0f &&
                                     actionTarget <= 0f && hSpeed < 2f ? 1f : 0f;
                mysticBlend = DampAsymmetric(mysticBlend, mysticTarget, dt, 0.3f, 3f);

                // ---- 2. Environnement : sondes, abri, donjon, plafond ----
                Vector3 predShift = flatVel * 0.35f;
                if (predShift.magnitude > 2.5f)
                {
                    predShift *= 2.5f / predShift.magnitude;
                }
                UpdateEnvironment(player, __instance, origin + predShift, right, eye.forward, dt, hSpeed, flatVel);

                float shelterTarget = player.InShelter() ? 1f : Mathf.Clamp01(player.m_coverPercentage);
                shelterBlend = DampAsymmetric(shelterBlend, shelterTarget, dt, 2f, 1f);
                dungeonProbeTimer -= dt;
                if (dungeonProbeTimer <= 0f)
                {
                    dungeonProbeTimer = 0.25f;
                    dungeonRaw = Location.IsInsideNoBuildLocation(player.transform.position);
                }
                dungeonBlend = DampAsymmetric(dungeonBlend, dungeonRaw ? 1f : 0f, dt, 2f, 1f);
                float interiorBlend = Mathf.Max(shelterBlend, dungeonBlend);

                float ceilingEff = Mathf.Min(ceilingClear, ceilingFar);
                if (ceilingEff < 0.8f + shelterBlend * 0.5f)
                {
                    ceilingBlocked = true;
                }
                else if (ceilingEff > 1.1f + shelterBlend * 0.7f)
                {
                    ceilingBlocked = false;
                }

                float roomBehind = Mathf.Min(rearClear, rearFar) - __instance.m_distance;
                float sideAvg = (rightClear + leftClear) * 0.5f;
                float confineTarget = 1f - Mathf.Clamp01(Mathf.Min(sideAvg, frontClear * 0.7f + 0.6f, roomBehind + 1f) / 3f);
                confineTarget = Mathf.Max(confineTarget, interiorBlend * 0.75f);
                confineBlend = DampAsymmetric(confineBlend, confineTarget, dt, 1.2f, 0.8f);

                float sprintRoomRaw = Mathf.Clamp01((roomBehind - 0.3f) / 2.5f) * (1f - interiorBlend * 0.7f);
                sprintRoomSm = DampAsymmetric(sprintRoomSm, sprintRoomRaw, dt, 1f, 2f);
                float sprintRoom = sprintRoomSm;

                UpdateShoulder(player, dt, mysticBlend, sprintBlend);

                // ---- 3. Cadrage par contexte (couches) ----
                float leanScale = Mathf.Clamp01((6f - __instance.m_distance) / 3f);
                Vector3 leadTarget = flatVel * 0.18f;
                if (leadTarget.magnitude > 0.5f)
                {
                    leadTarget *= 0.5f / leadTarget.magnitude;
                }
                float leadRate = (4f - confineBlend * 2f) * (1f - mysticBlend * 0.7f);
                leadLat = Mathf.MoveTowards(leadLat, Vector3.Dot(leadTarget, right), dt * leadRate);
                leadFwd = Mathf.MoveTowards(leadFwd, Vector3.Dot(leadTarget, eye.forward), dt * leadRate);

                // Explore : base over-shoulder, basse (tête sous le crosshair,
                // horizon dégagé ; le mystique et la monte remontent).
                float shoulder = ModConfig.SeidrShoulderOffset.Value * shoulderSign;
                float pullBack = ModConfig.SeidrDistanceBoost.Value;
                float height = ModConfig.SeidrHeightOffset.Value;
                float fovAdd = 0f;

                // Sprint : recul + FOV + rebond, à plein dehors, neutre au mur.
                pullBack += sprintBlend * ModConfig.SeidrSprintDistance.Value * sprintRoom;
                height += sprintBlend * 0.08f * sprintRoom;
                fovAdd += sprintBlend * ModConfig.SeidrSprintFov.Value * sprintRoom;
                float strideBob = Mathf.Sin(now * (9f + forwardSpeed * 0.4f)) * 0.035f * sprintBlend * sprintRoom;

                // Combat : recentrée, zoom avant, FOV élargi.
                shoulder *= 1f - combatBlend * 0.4f;
                pullBack -= combatBlend * ModConfig.SeidrCombatZoom.Value;
                fovAdd += combatBlend * CombatFovAdd;

                // Action : punch bref.
                pullBack += actionBlend * 0.35f * sprintRoom;
                fovAdd += actionBlend * ActionFovAdd * sprintRoom;

                // Mystique : éloignée, dérive lente, souffle vertical.
                shoulder = shoulder * (1f - mysticBlend * 0.55f)
                           + Mathf.Sin(now * 0.39f) * 0.18f * mysticBlend * sway;
                pullBack += mysticBlend * 0.7f;
                height += mysticBlend * 0.3f + Mathf.Sin(now * 1.05f) * 0.05f * mysticBlend * sway;

                // Intérieur / serré / plafond bas : hanche, resserrée.
                float hipBlend = Mathf.Max(interiorBlend, confineBlend);
                if (ceilingBlocked)
                {
                    hipBlend = Mathf.Max(hipBlend, 0.85f);
                }

                pullBack = Mathf.Lerp(pullBack, Mathf.Min(pullBack, 0.4f), interiorBlend);
                shoulder = Mathf.Lerp(shoulder, shoulder * 0.5f, interiorBlend);
                height = Mathf.Lerp(height, ModConfig.SeidrInteriorHeight.Value, hipBlend);

                // Look-ahead : en explo on accompagne vers l'avant (intime),
                // au sprint on lâche du champ derrière (vitesse).
                shoulder += leadLat * leanScale;
                pullBack += leadFwd * Mathf.Lerp(-1f, 0.6f, sprintBlend) * (1f - confineBlend * 0.5f);

                if (riding)
                {
                    pullBack += 0.45f;
                    height += 0.5f;
                }

                // ---- 4. Sécurité collision (avant celle du jeu, anti-pompage) ----
                allowPull = DampAsymmetric(allowPull, Mathf.Max(0.15f, roomBehind - 0.3f), dt, 0.8f, 8f + hSpeed * 1.5f);
                pullBack = Mathf.Min(pullBack, allowPull);
                float sideClear = targetSign > 0f ? rightClear : leftClear;
                allowShoulder = DampAsymmetric(allowShoulder, Mathf.Max(0.05f, sideClear - 0.3f), dt, 1.5f, 8f);
                float shoulderMag = Mathf.Min(Mathf.Abs(shoulder), allowShoulder);
                shoulder = (shoulder >= 0f ? 1f : -1f) * shoulderMag;
                shoulder *= 1f - confineBlend * 0.35f;

                // ---- 5. Lissage par axe + respiration ----
                float breath = sway * (0.35f + 0.65f * mysticBlend) * (1f - sprintBlend * 0.8f);
                Vector3 target = right * (shoulder + Mathf.Sin(now * 1.34f) * 0.02f * breath)
                                 + back * pullBack
                                 + up * (height + Mathf.Sin(now * 1.0f + 1.7f) * 0.015f * breath + strideBob);

                float smoothTime = Mathf.Clamp(1.6f / Mathf.Max(1f, ModConfig.SeidrSmoothness.Value), 0.06f, 1f);
                smoothTime *= (1f + confineBlend * 2.5f + mysticBlend * 0.5f) / (1f + actionBlend * 1.5f + sprintBlend * 0.8f);
                smoothTime = Mathf.Clamp(smoothTime, 0.05f, 1f);
                float latT = Vector3.Dot(target, right);
                float verT = Vector3.Dot(target, up);
                float boomT = Vector3.Dot(target, back);
                // Deadzone : uniquement le micro-pompage. Une vraie descente
                // (hanche) ne doit pas être avalée par le confine.
                if (verT >= smVer && Mathf.Abs(verT - smVer) < 0.08f + confineBlend * 0.12f)
                {
                    verT = smVer;
                }
                else if (verT < smVer && Mathf.Abs(verT - smVer) < 0.04f)
                {
                    verT = smVer;
                }
                if (ceilingBlocked && verT > smVer)
                {
                    verT = smVer;
                }
                smLat = Mathf.SmoothDamp(smLat, latT, ref velLat, smoothTime * 0.8f, Mathf.Infinity, dt);
                float verSmooth = verT < smVer
                    ? smoothTime * 1.1f
                    : smoothTime * (1.4f + confineBlend * 2f);
                smVer = Mathf.SmoothDamp(smVer, verT, ref velVer, verSmooth, Mathf.Infinity, dt);
                smBoom = Mathf.SmoothDamp(smBoom, boomT, ref velBoom, smoothTime, Mathf.Infinity, dt);
                extraOffset = right * smLat + up * smVer + back * smBoom;
                __result += extraOffset;

                // ---- 6. Sorties rotation / FOV / trauma ----
                pitchDrift = -1.2f * mysticBlend + Mathf.Sin(now * 0.7f + 0.6f) * 0.15f * breath;
                fovTarget = __instance.m_fovBase + fovAdd + Mathf.Sin(now * 0.7f) * 1.2f * mysticBlend;

                if (dodging && !wasDodging)
                {
                    trauma = Mathf.Min(1f, trauma + 0.25f);
                }
                if (prevVy < -9f && velocity.y > -1f)
                {
                    trauma = Mathf.Min(1f, trauma + 0.3f);
                }
                wasDodging = dodging;
                prevVy = velocity.y;
                trauma = Mathf.Max(0f, trauma - dt * 1.8f);
            }
        }

        private static float DampAsymmetric(float current, float target, float dt, float upSpeed, float downSpeed)
        {
            return Mathf.MoveTowards(current, target, dt * (target > current ? upSpeed : downSpeed));
        }

        internal static void ManualSwap()
        {
            targetSign = -targetSign;
            lastSwapTime = Time.time;
            autoResumeAt = Time.time + CameraUtil.ManualHoldSeconds;
            pendingSide = 0f;
            pendingTime = 0f;
            Jotunn.Logger.LogInfo(targetSign > 0f ? "Épaule droite" : "Épaule gauche");
        }

        private static void UpdateEnvironment(Player player, GameCamera camera, Vector3 origin, Vector3 right, Vector3 forward, float dt, float speed, Vector3 flatVel)
        {
            envProbeTimer -= dt;
            if (envProbeTimer > 0f)
            {
                return;
            }

            envProbeTimer = Mathf.Lerp(0.2f, 0.07f, Mathf.Clamp01(speed / 7f));
            int mask = camera ? camera.m_blockCameraMask.value : Physics.DefaultRaycastLayers;
            frontClear = CameraUtil.ProbeClearance(player, origin, forward, 4f, mask);
            float rearMax = (camera ? camera.m_distance : 4f) + 3.5f;
            rearClear = CameraUtil.ProbeClearance(player, origin, -forward, rearMax, mask);
            rightClear = CameraUtil.ProbeClearance(player, origin, right, 3f, mask);
            leftClear = CameraUtil.ProbeClearance(player, origin, -right, 3f, mask);
            float camDist = camera ? camera.m_distance : 4f;
            // Double sonde : proche + devant. On n'étend ni ne remonte
            // jamais pour devoir reculer juste après.
            Vector3 futureShift = flatVel * 0.5f;
            if (futureShift.magnitude > 2.5f)
            {
                futureShift *= 2.5f / futureShift.magnitude;
            }
            rearFar = CameraUtil.ProbeClearance(player, origin + futureShift, -forward, rearMax, mask);
            Vector3 camEst = origin - forward * camDist + Vector3.up * smVer;
            ceilingClear = CameraUtil.ProbeClearance(player, camEst, Vector3.up, 2.5f, mask);
            ceilingFar = CameraUtil.ProbeClearance(player, camEst + futureShift, Vector3.up, 2.5f, mask);
        }

        private static void UpdateShoulder(Player player, float dt, float mystic, float sprint)
        {
            // Transition douce (ease in-out), jamais sèche : anti-nausée.
            shoulderSign = Mathf.SmoothDamp(shoulderSign, targetSign, ref shoulderVel, 0.45f * (1f + confineBlend * 1.5f), Mathf.Infinity, dt);

            // Calme mystique : on ne change plus d'épaule.
            if (mystic > 0.6f || Time.time < autoResumeAt)
            {
                return;
            }

            // Au sprint, on verrouille l'épaule sauf si elle est vraiment bouchée.
            float sideNow = targetSign > 0f ? rightClear : leftClear;
            if (sprint > 0.5f && sideNow > 0.5f)
            {
                return;
            }

            shoulderProbeTimer -= dt;
            if (shoulderProbeTimer > 0f)
            {
                return;
            }

            shoulderProbeTimer = ShoulderProbeStep;

            float rightScore = rightClear;
            float leftScore = leftClear;

            if (player)
            {
                Vector3 velocity = player.GetVelocity();
                velocity.y = 0f;
                if (velocity.sqrMagnitude > 0.35f)
                {
                    Transform eye = player.m_eye ? player.m_eye : player.transform;
                    float lateral = Vector3.Dot(velocity.normalized, eye.right);
                    rightScore += Mathf.Max(0f, lateral) * 0.6f;
                    leftScore += Mathf.Max(0f, -lateral) * 0.6f;
                }
            }

            bool inCombat = combatBlend > 0.45f;
            float desired = targetSign;
            const float margin = 0.9f;
            if (inCombat && (targetSign > 0f ? rightClear : leftClear) > 0.7f)
            {
                desired = targetSign;
            }
            else if (leftScore > rightScore + margin)
            {
                desired = -1f;
            }
            else if (rightScore > leftScore + margin)
            {
                desired = 1f;
            }

            // Le nouveau côté doit gagner durablement, pas sur un pic.
            if (desired == targetSign)
            {
                pendingSide = 0f;
                pendingTime = 0f;
                return;
            }

            if (pendingSide != desired)
            {
                pendingSide = desired;
                pendingTime = 0f;
                return;
            }

            pendingTime += ShoulderProbeStep;
            if (pendingTime >= 0.5f && Time.time - lastSwapTime > 1.5f)
            {
                targetSign = desired;
                lastSwapTime = Time.time;
                pendingSide = 0f;
                pendingTime = 0f;
            }
        }

        [HarmonyPatch(typeof(GameCamera), "GetCameraPosition")]
        private static class GetCameraPositionPatch
        {
            private static void Postfix(GameCamera __instance, float dt, ref Vector3 pos, ref Quaternion rot)
            {
                Player player = Player.m_localPlayer;
                if (ShouldSkip(__instance, player))
                {
                    if (ModConfig.CameraMode != null && ModConfig.CameraMode.Value == ShoulderCameraMode.Seidr
                        && !CinematicIdleCamera.IsActive)
                    {
                        ApplyShake(ref pos, ref rot);
                    }

                    return;
                }

                ApplyShake(ref pos, ref rot);
            }
        }

        // Secousse : bruit cohérent (Perlin), rotation dominante, roulis plafonné anti-nausée.
        private static void ApplyShake(ref Vector3 pos, ref Quaternion rot)
        {
            float shake = trauma * trauma;
            float yawShake = 0f;
            float pitchShake = 0f;
            float rollShake = 0f;
            Vector3 posShake = Vector3.zero;
            if (shake > 0.0001f)
            {
                float t = Time.time * 28f;
                yawShake = (Mathf.PerlinNoise(t, 3.7f) - 0.5f) * 2f * 1.2f * shake;
                pitchShake = (Mathf.PerlinNoise(t, 9.1f) - 0.5f) * 2f * 0.9f * shake;
                rollShake = (Mathf.PerlinNoise(t, 5.3f) - 0.5f) * 2f * 2.2f * shake;
                posShake = rot * new Vector3(
                    (Mathf.PerlinNoise(t, 1.3f) - 0.5f) * 0.1f * shake,
                    (Mathf.PerlinNoise(t, 7.9f) - 0.5f) * 0.1f * shake,
                    0f);
            }

            float totalRoll = Mathf.Clamp(rollShake, -2.5f, 2.5f);
            float totalPitch = pitchDrift + pitchShake;
            if (Mathf.Abs(totalRoll) > 0.01f || Mathf.Abs(totalPitch) > 0.01f || Mathf.Abs(yawShake) > 0.01f)
            {
                rot *= Quaternion.Euler(totalPitch, yawShake, totalRoll);
            }

            pos += posShake;
        }

        [HarmonyPatch(typeof(GameCamera), "UpdateCamera")]
        private static class UpdateCameraPatch
        {
            private static void Postfix(GameCamera __instance)
            {
                Player player = Player.m_localPlayer;
                if (ShouldSkip(__instance, player))
                {
                    if (ModConfig.CameraMode != null && ModConfig.CameraMode.Value == ShoulderCameraMode.Seidr
                        && !CinematicIdleCamera.IsActive)
                    {
                        // Arc / lit / build : rendre le FOV à vanilla. On ne
                        // drive que si on l'avait déjà pris. Un fovTarget à 0
                        // passé à SetTempFOV fait osciller m_fov (inertie 1.6)
                        // jusqu'à la fin du tir.
                        if (appliedFov && fovTarget > 1f)
                        {
                            fovTarget = Mathf.MoveTowards(fovTarget, __instance.m_fovBase, Time.deltaTime * FovFadeRate);
                            CameraUtil.ApplyTempFov(__instance, fovTarget, ref appliedFov);
                        }
                        else
                        {
                            CameraUtil.ClearTempFov(__instance, ref appliedFov);
                        }
                    }
                    else
                    {
                        CameraUtil.ClearTempFov(__instance, ref appliedFov);
                    }

                    return;
                }

                CameraUtil.ApplyTempFov(__instance, fovTarget, ref appliedFov);
            }
        }

        [HarmonyPatch(typeof(CameraEffects), "UpdateDOF")]
        private static class UpdateDofPatch
        {
            private static void Postfix(CameraEffects __instance)
            {
                Player player = Player.m_localPlayer;
                GameCamera camera = GameCamera.instance;
                float weight = MysticDofWeight();
                if (!ShouldSkip(camera, player) && !CinematicIdleCamera.IsActive && weight >= 0.02f)
                {
                    ApplyMysticDof(__instance, player, weight);
                    return;
                }

                if (!CinematicIdleCamera.IsActive && mysticDof.Saved && weight >= 0.02f
                    && camera && ModConfig.CameraMode != null && ModConfig.CameraMode.Value == ShoulderCameraMode.Seidr)
                {
                    ApplyMysticDof(__instance, player, weight);
                    return;
                }

                mysticDof.Restore(__instance);
            }
        }

        private static float MysticDofWeight()
        {
            float sway = ModConfig.SeidrSway != null ? Mathf.Clamp01(ModConfig.SeidrSway.Value) : 0f;
            float interior = Mathf.Max(confineBlend, shelterBlend, dungeonBlend);
            float w = mysticBlend * sway * (1f - interior);
            Player player = Player.m_localPlayer;
            Camera main = Camera.main;
            if (main && player)
            {
                w *= Mathf.Clamp01(Mathf.InverseLerp(1.2f, 3.5f, HeadDistance(player, main)));
            }

            return Mathf.Clamp01(w);
        }

        private static float HeadDistance(Player player, Camera camera)
        {
            if (!player || !camera)
            {
                return 4f;
            }

            Vector3 head = player.GetHeadPoint();
            if (head.sqrMagnitude < 0.01f)
            {
                head = player.transform.position + Vector3.up * 1.2f;
            }

            return Vector3.Distance(camera.transform.position, head);
        }

        private static void ApplyMysticDof(CameraEffects effects, Player player, float weight)
        {
            if (!effects || !player || !mysticDof.Capture(effects))
            {
                return;
            }

            Camera main = Camera.main;
            float dist = HeadDistance(player, main);
            float close = Mathf.Clamp01(Mathf.InverseLerp(3.5f, 1.2f, dist));
            float w = Mathf.Clamp01(weight);
            float focalSize = Mathf.Lerp(0.08f, 0.4f, close);
            mysticDof.Push(
                effects,
                Mathf.Lerp(mysticDof.SavedBlur, 1.2f + 2f * w, w),
                Mathf.Lerp(mysticDof.SavedFocalSize, focalSize, w),
                Mathf.Lerp(mysticDof.SavedAperture, 0.3f, w),
                null,
                dist,
                close < 0.5f);
        }
    }
}
