using HarmonyLib;
using UnityEngine;

namespace Qmod
{
    internal static class YoteiCamera
    {
        private static Vector3 extraOffset;
        private static float sprintBlend;
        private static float combatBlend;
        private static bool appliedSprintFov;
        private static float shoulderSign = 1f;
        private static float targetSign = 1f;
        private static float lastSwapTime = -10f;
        private static float autoResumeAt;
        private static float shoulderProbeTimer;
        private static readonly RaycastHit[] probeHits = new RaycastHit[8];

        private static bool ShouldSkip(GameCamera camera, Player player)
        {
            if (!ModConfig.YoteiCameraEnabled.Value)
            {
                return true;
            }

            if (CinematicIdleCamera.IsActive)
            {
                return true;
            }

            if (!camera || !player)
            {
                return true;
            }

            if (camera.m_freeFly || camera.m_distance <= 0f)
            {
                return true;
            }

            if (player.InBed() || player.InPlaceMode() || player.IsDrawingBow())
            {
                return true;
            }

            return false;
        }

        [HarmonyPatch(typeof(GameCamera), "GetCameraOffset")]
        private static class GetCameraOffsetPatch
        {
            private static void Postfix(GameCamera __instance, Player player, ref Vector3 __result)
            {
                if (ShouldSkip(__instance, player))
                {
                    extraOffset = Vector3.zero;
                    sprintBlend = 0f;
                    combatBlend = 0f;
                    return;
                }

                float dt = Time.deltaTime;
                float sprintTarget = (player.IsRunning() && !player.IsSneaking()) || MagicBush.IsThrusting ? 1f : 0f;
                float combatTarget = player.UseMeleeCamera() || player.InAttack() || player.IsBlocking() ? 1f : 0f;
                if (MagicBush.IsRiding)
                {
                    combatTarget = 0f;
                }
                sprintBlend = Mathf.MoveTowards(sprintBlend, sprintTarget, dt * 2.2f);
                combatBlend = Mathf.MoveTowards(combatBlend, combatTarget, dt * 3.5f);

                Transform eye = player.m_eye ? player.m_eye : player.transform;
                Vector3 right = eye.right;
                Vector3 up = Vector3.up;
                Vector3 back = -eye.forward;
                Vector3 origin = eye.position;

                UpdateShoulder(player, __instance, origin, right, eye.forward, dt, combatBlend > 0.45f);

                float shoulder = ModConfig.YoteiShoulderOffset.Value * (1f - combatBlend * 0.35f) * shoulderSign;
                float pullBack = ModConfig.YoteiDistanceBoost.Value
                                 + sprintBlend * ModConfig.YoteiSprintDistance.Value
                                 - combatBlend * ModConfig.YoteiCombatZoom.Value;
                float height = ModConfig.YoteiHeightOffset.Value + sprintBlend * 0.08f;
                if (MagicBush.IsRiding)
                {
                    pullBack += 0.45f;
                    height += 0.5f;
                }

                Vector3 target = right * shoulder + back * pullBack + up * height;
                extraOffset = Vector3.Lerp(extraOffset, target, 1f - Mathf.Exp(-ModConfig.YoteiSmoothness.Value * dt));
                __result += extraOffset;
            }
        }

        internal static void ManualSwap()
        {
            targetSign = -targetSign;
            lastSwapTime = Time.time;
            autoResumeAt = Time.time + 12f;
            Jotunn.Logger.LogInfo(targetSign > 0f ? "Épaule droite" : "Épaule gauche");
        }

        private static void UpdateShoulder(Player player, GameCamera camera, Vector3 origin, Vector3 right, Vector3 forward, float dt, bool inCombat)
        {
            shoulderSign = Mathf.MoveTowards(shoulderSign, targetSign, dt * 2.4f);

            if (!ModConfig.YoteiAutoShoulder.Value)
            {
                targetSign = 1f;
                return;
            }

            if (Time.time < autoResumeAt)
            {
                return;
            }

            shoulderProbeTimer -= dt;
            if (shoulderProbeTimer > 0f)
            {
                return;
            }

            shoulderProbeTimer = 0.12f;
            int mask = camera ? camera.m_blockCameraMask.value : Physics.DefaultRaycastLayers;
            float rightClear = ProbeClearance(player, origin, right, 2.8f, mask);
            float leftClear = ProbeClearance(player, origin, -right, 2.8f, mask);
            float rightAhead = ProbeClearance(player, origin, (right + forward * 0.45f).normalized, 3.2f, mask);
            float leftAhead = ProbeClearance(player, origin, (-right + forward * 0.45f).normalized, 3.2f, mask);

            float rightScore = rightClear + rightAhead * 0.55f;
            float leftScore = leftClear + leftAhead * 0.55f;

            Vector3 velocity = player.GetVelocity();
            velocity.y = 0f;
            if (velocity.sqrMagnitude > 0.35f)
            {
                float lateral = Vector3.Dot(velocity.normalized, right);
                rightScore += Mathf.Max(0f, lateral) * 1.15f;
                leftScore += Mathf.Max(0f, -lateral) * 1.15f;
            }

            float lookX = ZInput.GetMouseDelta().x;
            if (Mathf.Abs(lookX) > 1.5f)
            {
                float turn = Mathf.Sign(lookX);
                rightScore += Mathf.Max(0f, turn) * 0.55f;
                leftScore += Mathf.Max(0f, -turn) * 0.55f;
            }

            float desired = targetSign;
            const float margin = 0.55f;
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

            if (desired != targetSign && Time.time - lastSwapTime > 0.7f)
            {
                targetSign = desired;
                lastSwapTime = Time.time;
            }
        }

        private static float ProbeClearance(Player player, Vector3 origin, Vector3 direction, float maxDistance, int mask)
        {
            if (direction.sqrMagnitude < 0.001f)
            {
                return maxDistance;
            }

            direction.Normalize();
            int count = Physics.SphereCastNonAlloc(origin, 0.14f, direction, probeHits, maxDistance, mask, QueryTriggerInteraction.Ignore);
            float best = maxDistance;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = probeHits[i];
                if (!hit.collider)
                {
                    continue;
                }

                Transform root = hit.collider.transform.root;
                if (root == player.transform || hit.collider.GetComponentInParent<Player>())
                {
                    continue;
                }

                if (hit.distance < best)
                {
                    best = hit.distance;
                }
            }

            return best;
        }

        [HarmonyPatch(typeof(GameCamera), "UpdateCamera")]
        private static class UpdateCameraPatch
        {
            private static void Postfix(GameCamera __instance)
            {
                Player player = Player.m_localPlayer;
                if (ShouldSkip(__instance, player))
                {
                    if (appliedSprintFov && !CinematicIdleCamera.IsActive)
                    {
                        __instance.ResetTempFOV();
                        appliedSprintFov = false;
                    }

                    return;
                }

                if (sprintBlend > 0.05f)
                {
                    __instance.SetTempFOV(__instance.m_fovBase + ModConfig.YoteiSprintFov.Value * sprintBlend, 1.6f);
                    appliedSprintFov = true;
                }
                else if (appliedSprintFov)
                {
                    __instance.ResetTempFOV();
                    appliedSprintFov = false;
                }
            }
        }
    }
}
