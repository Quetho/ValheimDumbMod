using UnityEngine;

namespace Qmod
{
    // Skip, sondes et input partagés par Yotei / AutoShoulder / Seidr / Heidr / cinématique.
    internal static class CameraUtil
    {
        internal const float ManualHoldSeconds = 6f;
        internal const float SkipFadeRate = 8f;

        private static readonly RaycastHit[] probeHits = new RaycastHit[8];

        internal static bool ShouldSkip(ShoulderCameraMode mode, GameCamera camera, Player player)
        {
            if (ModConfig.CameraMode == null || ModConfig.CameraMode.Value != mode)
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

            return player.InBed() || player.InPlaceMode() || player.IsDrawingBow();
        }

        internal static float FadeExp(float dt)
        {
            return Mathf.Exp(-SkipFadeRate * dt);
        }

        // Fondu de pause (arc / lit / build) : offset et blends sprint/combat.
        internal static void FadePause(ref Vector3 extraOffset, ref float sprintBlend, ref float combatBlend, float dt)
        {
            float k = FadeExp(dt);
            extraOffset *= k;
            sprintBlend *= k;
            combatBlend *= k;
            if (extraOffset.sqrMagnitude < 0.000001f && sprintBlend < 0.01f && combatBlend < 0.01f)
            {
                extraOffset = Vector3.zero;
                sprintBlend = 0f;
                combatBlend = 0f;
            }
        }

        internal static void ApplyTempFov(GameCamera camera, float target, ref bool applied)
        {
            if (!camera)
            {
                return;
            }

            if (Mathf.Abs(target - camera.m_fovBase) > 0.05f)
            {
                camera.SetTempFOV(target, 1.6f);
                applied = true;
            }
            else if (applied)
            {
                camera.ResetTempFOV();
                applied = false;
            }
        }

        internal static void ClearTempFov(GameCamera camera, ref bool applied)
        {
            if (applied && camera && !CinematicIdleCamera.IsActive)
            {
                camera.ResetTempFOV();
                applied = false;
            }
        }

        internal static float ProbeClearance(Player player, Vector3 origin, Vector3 direction, float maxDistance, int mask)
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
                if (!hit.collider || hit.distance >= best)
                {
                    continue;
                }

                Transform root = hit.collider.transform.root;
                if (player && (root == player.transform || hit.collider.GetComponentInParent<Player>()))
                {
                    continue;
                }

                best = hit.distance;
            }

            return best;
        }

        internal static bool HasPlayerInput(int ignoreUntilFrame = -1)
        {
            if (ignoreUntilFrame >= 0 && Time.frameCount <= ignoreUntilFrame)
            {
                return false;
            }

            Vector2 mouse = ZInput.GetMouseDelta();
            if (mouse.sqrMagnitude > 4f)
            {
                return true;
            }

            if (Mathf.Abs(ZInput.GetMouseScrollWheel()) > 0.01f)
            {
                return true;
            }

            if (ZInput.GetMouseButton(0) || ZInput.GetMouseButton(1) || ZInput.GetMouseButton(2))
            {
                return true;
            }

            if (ZInput.GetJoyLeftStick().sqrMagnitude > 0.05f || ZInput.GetJoyRightStick().sqrMagnitude > 0.05f)
            {
                return true;
            }

            if (ZInput.GetJoyLTrigger() > 0.25f || ZInput.GetJoyRTrigger() > 0.25f)
            {
                return true;
            }

            if (Input.anyKeyDown)
            {
                return true;
            }

            return ZInput.GetButton("Forward") || ZInput.GetButton("Backward") || ZInput.GetButton("Left") ||
                   ZInput.GetButton("Right") || ZInput.GetButton("Jump") || ZInput.GetButton("Attack") ||
                   ZInput.GetButton("SecondAttack") || ZInput.GetButton("Block") || ZInput.GetButton("Use") ||
                   ZInput.GetButton("Hide") || ZInput.GetButton("Crouch") || ZInput.GetButton("Run") ||
                   ZInput.GetButton("Dodge") || ZInput.GetButtonDown("Inventory") || ZInput.GetButtonDown("Map");
        }
    }
}
