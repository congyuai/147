using System.Collections.Generic;
using UnityEngine;

namespace Dustline
{
    public sealed class BotController : MonoBehaviour
    {
        enum State { Patrol, Search, Attack }
        GameSession session;
        Health health, target;
        CharacterController motor;
        Transform body;
        State state;
        Vector3 goal, lastKnown;
        List<Vector3> path = new List<Vector3>();
        int waypoint, magazine = 20;
        float thinkAt, pathAt, shotAt, lostAt, searchUntil, reloadUntil;
        readonly System.Random random = new System.Random();
        Vector3 Eye => transform.position + Vector3.up * 1.5f;

        public void Initialize(GameSession owner, int team, int seed)
        {
            session = owner;
            motor = gameObject.AddComponent<CharacterController>();
            motor.height = 1.8f;
            motor.radius = 0.35f;
            motor.center = Vector3.up * 0.9f;
            health = gameObject.AddComponent<Health>();
            health.Initialize(80, team);
            health.Hurt += OnHurt;
            health.Died += Die;
            var visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Destroy(visual.GetComponent<Collider>());
            visual.name = team == 1 ? "Raider" : "Scavenger";
            body = visual.transform;
            body.SetParent(transform, false);
            body.localPosition = Vector3.up * 0.9f;
            body.localScale = new Vector3(0.7f, 0.9f, 0.7f);
            var renderer = visual.GetComponent<Renderer>();
            renderer.sharedMaterial = session.World.DarkMaterial;
            var properties = new MaterialPropertyBlock();
            properties.SetColor("_BaseColor", team == 1 ? new Color(0.6f, 0.24f, 0.16f) : new Color(0.21f, 0.34f, 0.25f));
            renderer.SetPropertyBlock(properties);
            var weapon = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(weapon.GetComponent<Collider>());
            weapon.transform.SetParent(transform, false);
            weapon.transform.localPosition = new Vector3(0.25f, 1.2f, 0.4f);
            weapon.transform.localScale = new Vector3(0.12f, 0.12f, 0.6f);
            weapon.GetComponent<Renderer>().sharedMaterial = session.World.DarkMaterial;
            thinkAt = Time.time + seed * 0.027f;
            CombatNoise.Heard += OnNoise;
            PickPatrol();
        }

        void OnDisable() { CombatNoise.Heard -= OnNoise; }
        void OnNoise(Vector3 position, float radius, Health source)
        {
            if (source == null || source.Team == health.Team || !health.Alive || Vector3.Distance(position, transform.position) > radius) return;
            if (state == State.Attack) return;
            lastKnown = goal = position;
            state = State.Search;
            searchUntil = Time.time + 9;
            pathAt = 0;
        }
        void OnHurt(Health attacker)
        {
            if (attacker == null || !attacker.Alive) return;
            target = attacker;
            lastKnown = goal = attacker.transform.position;
            state = State.Search;
            searchUntil = Time.time + 10;
            pathAt = 0;
        }
        bool Visible(Health actor)
        {
            var delta = actor.AimPoint - Eye;
            if (delta.sqrMagnitude > 34 * 34) return false;
            if (state == State.Patrol && Vector3.Dot(transform.forward, delta.normalized) < 0.25f) return false;
            return Physics.Raycast(Eye, delta.normalized, out var hit, delta.magnitude + 0.3f, ~0, QueryTriggerInteraction.Ignore)
                && hit.collider.GetComponentInParent<Health>() == actor;
        }
        void Update()
        {
            if (!health.Alive || session.Phase != SessionPhase.Raid) return;
            if (Time.time >= thinkAt)
            {
                thinkAt = Time.time + 0.2f;
                Health seen = null;
                float nearest = float.PositiveInfinity;
                foreach (var actor in Health.Actors)
                {
                    if (!actor.Alive || actor.Team == health.Team) continue;
                    float distance = (actor.transform.position - transform.position).sqrMagnitude;
                    if (distance < nearest && Visible(actor)) { seen = actor; nearest = distance; }
                }
                if (seen != null)
                {
                    if (state != State.Attack) shotAt = Mathf.Max(shotAt, Time.time + 0.55f);
                    target = seen;
                    state = State.Attack;
                    lastKnown = seen.transform.position;
                    lostAt = Time.time;
                }
                else if (state == State.Attack && Time.time - lostAt > 0.6f)
                {
                    state = State.Search;
                    goal = lastKnown;
                    searchUntil = Time.time + 7;
                    pathAt = 0;
                }
                if (state == State.Search && Time.time > searchUntil) PickPatrol();
            }
            Vector3 movement = Vector3.zero;
            if (state == State.Attack && target != null && target.Alive)
            {
                Vector3 facing = lastKnown - transform.position;
                facing.y = 0;
                if (facing.sqrMagnitude > 0.01f) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(facing), 8 * Time.deltaTime);
                if (Visible(target)) Shoot();
            }
            else
            {
                if ((goal - transform.position).sqrMagnitude < 2 && state == State.Patrol) PickPatrol();
                if (Time.time >= pathAt)
                {
                    path = session.World.Navigation.Path(transform.position, goal);
                    waypoint = 0;
                    pathAt = Time.time + 1.5f + (float)random.NextDouble();
                }
                while (waypoint < path.Count && (path[waypoint] - transform.position).sqrMagnitude < 0.65f) waypoint++;
                if (waypoint < path.Count)
                {
                    movement = path[waypoint] - transform.position;
                    movement.y = 0;
                    movement = movement.normalized;
                    if (movement.sqrMagnitude > 0) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(movement), 7 * Time.deltaTime);
                }
            }
            motor.Move((movement * (state == State.Search ? 3.1f : 2.2f) + Vector3.down * 5) * Time.deltaTime);
        }
        void PickPatrol()
        {
            state = State.Patrol;
            goal = session.World.PatrolPoints[random.Next(session.World.PatrolPoints.Count)];
            pathAt = 0;
        }
        void Shoot()
        {
            if (Time.time < shotAt || Time.time < reloadUntil) return;
            if (magazine <= 0) { reloadUntil = Time.time + 2.8f; magazine = 20; return; }
            magazine--;
            shotAt = Time.time + 0.38f;
            var point = target.AimPoint + Random.insideUnitSphere * 0.7f;
            var direction = (point - Eye).normalized;
            var end = Eye + direction * 45;
            if (Physics.Raycast(Eye, direction, out var hit, 45, ~0, QueryTriggerInteraction.Ignore))
            {
                end = hit.point;
                hit.collider.GetComponentInParent<Health>()?.Damage(12, health);
            }
            CombatNoise.Emit(transform.position, 55, health);
            session.World.Trace(Eye + direction * 0.6f, end, Color.yellow);
        }
        void Die(Health killer)
        {
            CombatNoise.Heard -= OnNoise;
            motor.enabled = false;
            body.localRotation = Quaternion.Euler(90, 0, 0);
            body.localPosition = Vector3.up * 0.3f;
            var loot = session.World.CreateLoot(transform.position + transform.right * 0.8f, "阵亡者物资");
            session.LootTable.Fill(loot.Inventory, random);
            loot.Inventory.TryAdd("ammo", random.Next(12, 31), out _);
            if (killer == session.Player.Health) session.Kills++;
            enabled = false;
        }
    }
}
