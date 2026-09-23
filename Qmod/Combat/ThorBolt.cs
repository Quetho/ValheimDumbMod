using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Qmod
{
    internal static class ThorBolt
    {
        private const float Cooldown = 2.4f;
        private const float RaiseDelay = 0.38f;
        private const float PointDelay = 0.42f;
        private const float Range = 55f;
        private const float ImpactRadius = 4.5f;
        private const float BeamRadius = 2.8f;

        private static readonly RaycastHit[] hits = new RaycastHit[16];
        private static readonly Collider[] overlap = new Collider[48];
        private static readonly HashSet<int> smashed = new HashSet<int>();
        private static float nextCast;
        private static bool casting;
        private static int baseAimMask;
        private static bool aimMaskCached;

        internal static void Cast()
        {
            if (casting || Time.unscaledTime < nextCast || ThorTp.IsTraveling)
            {
                return;
            }

            Player player = Player.m_localPlayer;
            if (!ModConfig.IsOdin())
            {
                Util.NotifyPlayer(player, "Odin ne répond pas");
                return;
            }

            if (!CanCast(player) || Qmod.Instance == null)
            {
                return;
            }

            nextCast = Time.unscaledTime + Cooldown;
            Qmod.Instance.StartCoroutine(CastRoutine());
        }

        private static IEnumerator CastRoutine()
        {
            casting = true;
            Player player = Player.m_localPlayer;
            if (!CanCast(player))
            {
                casting = false;
                yield break;
            }

            player.StartEmote("challenge", true);
            yield return new WaitForSeconds(RaiseDelay);

            player = Player.m_localPlayer;
            if (!CanCast(player))
            {
                casting = false;
                yield break;
            }

            ThorLightning.Play(player.transform.position);

            yield return new WaitForSeconds(PointDelay * 0.45f);

            player = Player.m_localPlayer;
            if (!CanCast(player))
            {
                casting = false;
                yield break;
            }

            player.StartEmote("point", true);
            yield return new WaitForSeconds(PointDelay);

            player = Player.m_localPlayer;
            if (!CanCast(player))
            {
                casting = false;
                yield break;
            }

            FireBolt(player);
            casting = false;
        }

        private static void FireBolt(Player player)
        {
            Vector3 start = player.GetEyePoint();
            Vector3 end;
            RaycastHit hit;
            if (!TryAim(player, out end, out hit))
            {
                Vector3 look = AimDir();
                end = start + look * Range;
            }

            Vector3 dir = end - start;
            if (dir.sqrMagnitude < 0.01f)
            {
                dir = AimDir();
            }
            else
            {
                dir.Normalize();
            }

            ThorLightning.PlayDirected(start, end);
            ThorLightning.Play(end);
            smashed.Clear();
            SmashArea(player, Vector3.Lerp(start, end, 0.55f), dir, BeamRadius);
            SmashArea(player, end, dir, ImpactRadius);
        }

        private static bool TryAim(Player player, out Vector3 end, out RaycastHit hit)
        {
            end = default;
            hit = default;
            Vector3 origin;
            Vector3 dir;
            AimRay(out origin, out dir);
            int mask = AimMask();
            int count = Physics.RaycastNonAlloc(origin, dir, hits, Range, mask, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                RaycastHit candidate = hits[i];
                if (!candidate.collider || candidate.distance >= best)
                {
                    continue;
                }

                if (IsLocalPlayer(candidate, player))
                {
                    continue;
                }

                best = candidate.distance;
                hit = candidate;
                found = true;
            }

            if (!found)
            {
                return false;
            }

            end = hit.point;
            return true;
        }

        private static void SmashArea(Player attacker, Vector3 point, Vector3 dir, float radius)
        {
            int mask = AimMask();
            int count = Physics.OverlapSphereNonAlloc(point, radius, overlap, mask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider collider = overlap[i];
                if (!collider)
                {
                    continue;
                }

                Character character = collider.GetComponentInParent<Character>();
                if (character)
                {
                    if (character == attacker || smashed.Contains(character.GetInstanceID()))
                    {
                        continue;
                    }

                    smashed.Add(character.GetInstanceID());
                    HitData hit = MakeHit(attacker, dir, character.GetCenterPoint());
                    character.Damage(hit);
                    character.Stagger(dir);
                    continue;
                }

                IDestructible destructible = collider.GetComponentInParent<IDestructible>();
                if (destructible == null || ReferenceEquals(destructible, attacker))
                {
                    continue;
                }

                Component component = destructible as Component;
                int id = component ? component.GetInstanceID() : collider.GetInstanceID();
                if (smashed.Contains(id))
                {
                    continue;
                }

                smashed.Add(id);
                destructible.Damage(MakeHit(attacker, dir, point));
            }
        }

        private static HitData MakeHit(Player attacker, Vector3 dir, Vector3 point)
        {
            HitData hit = new HitData();
            hit.m_damage.m_blunt = 70f;
            hit.m_damage.m_lightning = 80f;
            hit.m_damage.m_chop = 100f;
            hit.m_damage.m_pickaxe = 40f;
            hit.m_toolTier = 2;
            hit.m_point = point;
            hit.m_dir = dir;
            hit.m_pushForce = 120f;
            hit.m_staggerMultiplier = 2.5f;
            hit.m_dodgeable = false;
            hit.m_blockable = false;
            hit.m_hitType = HitData.HitType.PlayerHit;
            hit.m_skill = Skills.SkillType.ElementalMagic;
            hit.SetAttacker(attacker);
            return hit;
        }

        private static void AimRay(out Vector3 origin, out Vector3 dir)
        {
            GameCamera camera = GameCamera.instance;
            if (camera && camera.m_camera)
            {
                origin = camera.m_camera.transform.position;
                dir = camera.m_camera.transform.forward;
                return;
            }

            Player player = Player.m_localPlayer;
            origin = player.GetEyePoint();
            dir = player.GetLookDir();
        }

        private static Vector3 AimDir()
        {
            Vector3 origin;
            Vector3 dir;
            AimRay(out origin, out dir);
            return dir;
        }

        private static int AimMask()
        {
            if (!aimMaskCached)
            {
                baseAimMask = LayerMask.GetMask(
                    "Default",
                    "static_solid",
                    "Default_small",
                    "piece",
                    "terrain",
                    "character",
                    "character_net",
                    "character_ghost",
                    "hitbox",
                    "vehicle");
                aimMaskCached = true;
            }

            int mask = baseAimMask;
            GameCamera camera = GameCamera.instance;
            if (camera)
            {
                mask |= camera.m_blockCameraMask;
            }

            return mask;
        }

        private static bool IsLocalPlayer(RaycastHit hit, Player player)
        {
            Transform t = hit.collider.transform;
            return t == player.transform || t.IsChildOf(player.transform);
        }

        private static bool CanCast(Player player)
        {
            return Util.CanAct(player) &&
                   !player.InBed() &&
                   !player.IsAttached() &&
                   !MagicBush.IsActive;
        }
    }
}
