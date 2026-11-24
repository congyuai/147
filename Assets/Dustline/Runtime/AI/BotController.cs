using System.Collections.Generic;
using UnityEngine;

namespace Dustline
{
    public sealed class BotController : MonoBehaviour
    {
        enum State { Patrol, Search, Attack }
        public BotRank Rank { get; private set; }
        GameSession session;
        BotTuning tuning;
        Health health, target;
        CharacterController motor;
        OperatorVisual operatorVisual;
        WeaponVisual weapon;
        StepCadence steps;
        State state;
        Vector3 goal, lastKnown;
        List<Vector3> path = new List<Vector3>();
        const int MagazineSize = 20;
        // The player's controller is on Ignore Raycast to exclude it from the player's own casts.
        const int CombatLayers = Physics.AllLayers;
        int waypoint, magazine = MagazineSize;
        float thinkAt, pathAt, shotAt, lostAt, searchUntil, reloadUntil;
        System.Random random;
        bool warnedPlayer;
        bool GuardsZone => Rank != BotRank.Regular;
        Vector3 Eye => transform.position + Vector3.up * 1.5f;

        public void Initialize(GameSession owner, int team, int seed) => Initialize(owner, team, seed, BotRank.Regular);

        public void Initialize(GameSession owner, int team, int seed, BotRank rank = BotRank.Regular)
        {
            session = owner;
            Rank = rank;
            tuning = BotTuning.For(rank);
            random = new System.Random(unchecked((seed + 1) * 486187739 ^ team * 16777619));
            motor = gameObject.AddComponent<CharacterController>();
            motor.height = 1.8f;
            motor.radius = 0.35f;
            motor.center = Vector3.up * 0.9f;
            health = gameObject.AddComponent<Health>();
            health.Initialize(tuning.Health, team);
            health.AimHeight = 1.25f;
            health.Hurt += OnHurt;
            health.Died += Die;
            operatorVisual = OperatorVisual.Create(transform,
                seed % 2 == 0 ? OperatorStyle.Male : OperatorStyle.Female, team, gameObject.layer);
            var weaponMount = operatorVisual.WeaponMount;
            weapon = WeaponVisual.Create(weaponMount, false);
            weapon.SetEquipped(true);
            operatorVisual.PoseArms(weapon.RightGrip, weapon.LeftGrip);
            thinkAt = Time.time + (float)random.NextDouble() * 0.3f;
            CombatNoise.Heard += OnNoise;
            PickPatrol();
        }

        void OnDisable() { CombatNoise.Heard -= OnNoise; }

        bool CanEngage(Health actor) => actor != null && actor.Alive && actor.Team != health.Team
            && (!GuardsZone || PortLayout.InHighValueArea(transform.position) && PortLayout.InHighValueArea(actor.transform.position));

        void OnNoise(Vector3 position, float radius, Health source)
        {
            if (!health.Alive || session.Phase != SessionPhase.Raid || state == State.Attack || !CanEngage(source)
                || (GuardsZone && !PortLayout.InHighValueArea(position))) return;
            float audible = Mathf.Min(radius, tuning.HearingRange);
            if ((position - transform.position).sqrMagnitude > audible * audible) return;
            StartSearch(position, GuardsZone ? 6f : 4f);
        }

        void OnHurt(Health attacker)
        {
            if (!health.Alive || state == State.Attack || !CanEngage(attacker)) return;
            Vector3 position = attacker.transform.position;
            if ((position - transform.position).sqrMagnitude <= tuning.HearingRange * tuning.HearingRange)
                StartSearch(position, GuardsZone ? 7f : 5f);
        }

        void StartSearch(Vector3 position, float duration)
        {
            if (state != State.Search || (goal - position).sqrMagnitude > 9f) pathAt = 0f;
            state = State.Search;
            target = null;
            goal = position;
            searchUntil = Time.time + duration;
        }

        bool Visible(Health actor)
        {
            var delta = actor.AimPoint - Eye;
            if (delta.sqrMagnitude > tuning.SightRange * tuning.SightRange) return false;
            if (state == State.Patrol && Vector3.Dot(transform.forward, delta.normalized) < 0.25f) return false;
            return Physics.Raycast(Eye, delta.normalized, out var hit, delta.magnitude + 0.3f, CombatLayers,
                QueryTriggerInteraction.Ignore) && hit.collider.GetComponentInParent<Health>() == actor;
        }
        void Update()
        {
            if (health == null || !health.Alive || session.Phase != SessionPhase.Raid) return;
            if (state == State.Attack && !CanEngage(target)) PickPatrol();
            if (state == State.Search && GuardsZone && (!PortLayout.InHighValueArea(goal) || !PortLayout.InHighValueArea(transform.position))) PickPatrol();
            if (Time.time >= thinkAt)
            {
                thinkAt = Time.time + 0.2f;
                Health seen = null;
                float nearest = float.PositiveInfinity;
                foreach (var actor in Health.Actors)
                {
                    if (!CanEngage(actor)) continue;
                    float distance = (actor.transform.position - transform.position).sqrMagnitude;
                    if (distance < nearest && Visible(actor)) { seen = actor; nearest = distance; }
                }
                if (seen != null)
                {
                    if (state != State.Attack || target != seen)
                        shotAt = Mathf.Max(shotAt, Time.time + tuning.ReactionSeconds / session.RaidThreat);
                    target = seen;
                    state = State.Attack;
                    lastKnown = seen.transform.position;
                    lostAt = Time.time;
                    if (Rank == BotRank.Boss && !warnedPlayer && seen == session.Player.Health)
                    {
                        warnedPlayer = true;
                        session.UI.Notify("重装首领已发现你！离开高价值区可脱离追击。");
                    }
                }
                else if (state == State.Attack && Time.time - lostAt > 0.6f)
                    StartSearch(lastKnown, GuardsZone ? 6f : 4f);
                if (state == State.Search && Time.time > searchUntil) PickPatrol();
            }
            Vector3 movement = Vector3.zero;
            if (state == State.Attack)
            {
                Vector3 facing = lastKnown - transform.position;
                facing.y = 0;
                if (facing.sqrMagnitude > 0.01f) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(facing), 8 * Time.deltaTime);
                if (Time.time >= shotAt && Time.time >= reloadUntil)
                {
                    if (Visible(target)) Shoot();
                    else shotAt = Time.time + 0.1f;
                }
            }
            else
            {
                if (state == State.Patrol && Time.time >= pathAt && (goal - transform.position).sqrMagnitude < 2f)
                    PickPatrol();
                if (Time.time >= pathAt)
                {
                    path = session.World.Navigation.Path(transform.position, goal);
                    waypoint = 0;
                    pathAt = Time.time + 1.5f + (float)random.NextDouble();
                    // A route to an interior target may otherwise take a detour out of the zone.
                    if (GuardsZone && path.Exists(point => !PortLayout.InHighValueArea(point))) path.Clear();
                    if (path.Count == 0 && state == State.Patrol) goal = PatrolGoal();
                }
                while (waypoint < path.Count)
                {
                    Vector3 offset = path[waypoint] - transform.position;
                    if (offset.x * offset.x + offset.z * offset.z > 0.0625f) break;
                    waypoint++;
                }
                if (waypoint < path.Count)
                {
                    if (!session.World.Navigation.TryLocalStep(transform.position, path[waypoint], motor, out movement))
                    {
                        movement = Vector3.zero;
                        pathAt = Mathf.Min(pathAt, Time.time + 0.45f);
                    }
                    else if (movement.sqrMagnitude > 0f)
                        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(movement), 7 * Time.deltaTime);
                }
            }
            float speed = movement.sqrMagnitude > 0.01f ? (state == State.Search ? tuning.SearchSpeed : tuning.PatrolSpeed) : 0f;
            Vector3 before = transform.position;
            Vector3 step = movement * (speed * Time.deltaTime);
            if (GuardsZone && PortLayout.InHighValueArea(before) && !PortLayout.InHighValueArea(before + step))
            {
                step = Vector3.zero;
                speed = 0f;
                PickPatrol();
            }
            motor.Move(step + Vector3.down * (5f * Time.deltaTime));
            Vector3 displacement = transform.position - before;
            if (steps.Advance(new Vector2(displacement.x, displacement.z).magnitude, motor.isGrounded, 1.38f))
                session.EnvironmentAudio.PlayFootstep(transform.position, 0.72f, false);
            operatorVisual.SetMotion(speed, false, false, Time.deltaTime);
            weapon.SetPose(state == State.Attack, speed, false, false, Time.deltaTime);
            operatorVisual.PoseArms(weapon.RightGrip, weapon.LeftGrip);
        }
        void PickPatrol()
        {
            state = State.Patrol;
            target = null;
            goal = PatrolGoal();
            pathAt = 0f;
        }

        Vector3 PatrolGoal()
        {
            if (!GuardsZone) return session.World.PickPatrolPoint(transform.position, random);
            Vector3 point = session.World.PickGuardPoint(transform.position, random);
            return PortLayout.InHighValueArea(point) ? point : PortLayout.HighValueCenter;
        }

        void Shoot()
        {
            if (magazine <= 0)
            {
                reloadUntil = Time.time + tuning.ReloadSeconds;
                magazine = MagazineSize;
                weapon.BeginReload(tuning.ReloadSeconds);
                return;
            }
            magazine--;
            shotAt = Time.time + tuning.ShotInterval + (float)random.NextDouble() * 0.20f;
            Vector3 eye = Eye;
            Vector3 delta = target.AimPoint - eye;
            Vector3 side = Vector3.Cross(Vector3.up, delta.normalized).normalized;
            float angle = (float)random.NextDouble() * 2f * Mathf.PI;
            float spread = (tuning.Spread + delta.magnitude * 0.012f) * Mathf.Sqrt((float)random.NextDouble());
            var direction = (delta + spread * (side * Mathf.Cos(angle) + Vector3.up * Mathf.Sin(angle))).normalized;
            float range = tuning.SightRange + 2f;
            var end = eye + direction * range;
            bool hitSomething = Physics.Raycast(eye, direction, out var hit, range, CombatLayers,
                QueryTriggerInteraction.Ignore);
            bool hitEnemy = false;
            if (hitSomething)
            {
                end = hit.point;
                Health struck = hit.collider.GetComponentInParent<Health>();
                hitEnemy = CanEngage(struck);
                if (hitEnemy) struck.DamageAt(tuning.Damage * session.RaidThreat, health, hit.point);
            }
            CombatNoise.Emit(transform.position, 55f, health);
            session.Effects?.Shot(weapon.Muzzle.position, end, hitSomething ? hit.normal : Vector3.zero, hitSomething, hitEnemy, false);
            weapon.Kick();
        }
        void Die(Health killer)
        {
            CombatNoise.Heard -= OnNoise;
            motor.enabled = false;
            weapon.SetEquipped(false);
            operatorVisual.Fall();
            string title = Rank == BotRank.Boss ? "首领物资" : Rank == BotRank.Elite ? "精英守卫物资" : "阵亡者物资";
            var loot = session.World.CreateLoot(transform.position + transform.right * 0.8f, title);
            session.LootTable.Fill(loot.Inventory, random);
            loot.Inventory.TryAdd("ammo", random.Next(12, 31), out _);
            if (killer == session.Player.Health) session.Kills++;
            enabled = false;
        }
    }
}