using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace Dustline.Tests
{
    public sealed class WeaponCombatRegressionTests
    {
        const BindingFlags InstancePrivate = BindingFlags.Instance | BindingFlags.NonPublic;
        static readonly Vector3 TestOrigin = new Vector3(1600, 50, -1600);
        readonly List<Object> created = new List<Object>();

        [TearDown]
        public void RemoveObjects()
        {
            for (int i = created.Count - 1; i >= 0; i--)
                if (created[i] != null) Object.DestroyImmediate(created[i]);
            created.Clear();
        }

        [Test]
        public void DamagedReportsActualLossAndWorldBearingExactlyOnce()
        {
            var target = AddObject("Target").AddComponent<Health>();
            var source = AddObject("Source").AddComponent<Health>();
            target.transform.rotation = Quaternion.Euler(0, 130, 0);
            source.transform.position = new Vector3(4, 3, 0);
            target.Initialize(30, 0);
            source.Initialize(100, 1);
            target.ArmorReduction = () => 0.5f;
            var reported = new List<(Vector3 direction, float lost)>();
            int hurt = 0, died = 0;
            target.Hurt += _ => hurt++;
            target.Died += _ => died++;
            target.Damaged += (direction, lost) => reported.Add((direction, lost));

            target.Damage(0, source);
            target.Damage(float.NaN, source);
            target.Damage(1e-10f, source);
            source.Initialize(100, 0);
            target.Damage(20, source);
            Assert.That(reported, Is.Empty);
            source.Initialize(100, 1);
            target.Damage(20, source);
            target.Damage(100, null);
            target.Damage(100, source);
            Assert.That(reported.Count, Is.EqualTo(2));
            Assert.That(Vector3.Distance(reported[0].direction, new Vector3(0.8f, 0.6f, 0)), Is.LessThan(0.0001f));
            Assert.That(reported[0].lost, Is.EqualTo(10));
            Assert.That(reported[1].direction, Is.EqualTo(Vector3.zero));
            Assert.That(reported[1].lost, Is.EqualTo(20));
            Assert.That(hurt, Is.EqualTo(2));
            Assert.That(died, Is.EqualTo(1));
        }

        [Test]
        public void EmptyMagazineStartsOnlyWithAmmoAndTakesTheReserveAtCompletion()
        {
            var f = new Fight(this, 0, 0);
            f.Tick(0.25f, fire: true);
            Assert.That(f.Weapon.Magazine.Reloading, Is.False);
            Assert.That(f.Weapon.Magazine.Rounds, Is.Zero);
            Assert.That(f.Bag.TryAdd("ammo", 5, out var error), Is.True, error);

            f.Tick(0.02f);
            Assert.That(f.Weapon.Magazine.Reloading, Is.True);
            Assert.That(f.Bag.Count("ammo"), Is.EqualTo(5));
            f.Bag.Consume("ammo", 2);
            int notifications = 0;
            f.Bag.Changed += () =>
            {
                notifications++;
                Assert.That(f.Equipped.LoadedAmmo, Is.EqualTo(3));
                Assert.That(f.Bag.Count("ammo"), Is.Zero);
                f.Weapon.SyncEquipment();
            };
            f.Tick(1.1f);
            Assert.That(f.Weapon.Magazine.Reloading, Is.False);
            Assert.That(f.Weapon.Magazine.Rounds, Is.EqualTo(3));
            Assert.That(f.Weapon.Reserve, Is.Zero);
            Assert.That(notifications, Is.EqualTo(1));
        }

        [Test]
        public void ForegroundMagazineFollowsTheReloadTimerAndReseatsOnCompletion()
        {
            var f = new Fight(this, 2, 12);
            var view = f.Actor.WeaponView;
            var prop = view.transform.Find("Magazine");
            Assert.That(prop, Is.Not.Null);
            Assert.That(prop.gameObject.layer, Is.EqualTo(FirstPersonView.Layer));
            Assert.That(prop.GetComponent<MeshFilter>().sharedMesh, Is.Not.SameAs(view.GetComponent<MeshFilter>().sharedMesh));
            Assert.That(prop.localPosition, Is.EqualTo(Vector3.zero));
            f.Tick(.25f, reload: true);
            float duration = f.Weapon.Magazine.Remaining;
            f.Tick(duration * .39f);
            view.SetPose(false, 0f, false, false, .016f);
            Assert.That(prop.gameObject.activeSelf, Is.True);
            Assert.That(prop.localPosition.y, Is.LessThan(-.12f));
            Assert.That(f.Bag.Count("ammo"), Is.EqualTo(12));
            f.Tick(duration * .10f);
            view.SetPose(false, 0f, false, false, .016f);
            Assert.That(prop.gameObject.activeSelf, Is.False);
            f.Tick(duration * .14f);
            view.SetPose(false, 0f, false, false, .016f);
            Assert.That(prop.gameObject.activeSelf, Is.True);
            Assert.That(prop.localPosition.y, Is.LessThan(-.10f));
            f.Tick(duration);
            Assert.That(f.Equipped.LoadedAmmo, Is.EqualTo(8));
            Assert.That(f.Bag.Count("ammo"), Is.EqualTo(6));
            Assert.That(prop.localPosition, Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void TreatmentCancelsReloadLocksCombatAndConsumesTheChosenStackOnce()
        {
            var f = new Fight(this, 2, 10);
            Assert.That(f.Bag.TryAdd("medkit", 2, out var error), Is.True, error);
            string first = f.Bag.Items[f.Bag.Items.Count - 2].InstanceId;
            string chosen = f.Bag.Items[f.Bag.Items.Count - 1].InstanceId;
            f.Actor.Health.Damage(65f, null);
            f.Tick(.24f, reload: true);
            f.Session.UseMedicine(chosen);
            Assert.That(f.Actor.IsTreating, Is.True);
            Assert.That(f.Weapon.Magazine.Reloading, Is.False);
            f.Tick(.21f, fire: true, reload: true);
            Invoke(f.Actor, "TickTreatment", .4f);
            var view = f.Actor.WeaponView;
            Invoke(view, "SetMedicineProgress", f.Actor.TreatmentProgress);
            view.SetPose(false, 0f, false, false, .10f);
            Assert.That(view.transform.Find("Medicine").gameObject.activeSelf, Is.True);
            Assert.That(f.Actor.Health.Current, Is.EqualTo(35f));
            Assert.That(f.Equipped.LoadedAmmo, Is.EqualTo(2));
            Assert.That(f.Bag.Count("ammo"), Is.EqualTo(10));
            Assert.That(f.Bag.Count("medkit"), Is.EqualTo(2));
            f.Session.UseMedicine(first);
            Invoke(f.Actor, "TickTreatment", 1f);
            Invoke(f.Actor, "TickTreatment", 1f);
            Assert.That(f.Actor.IsTreating, Is.False);
            Assert.That(f.Actor.Health.Current, Is.EqualTo(65f));
            Assert.That(f.Bag.Find(chosen), Is.Null);
            Assert.That(f.Bag.Find(first), Is.Not.Null);
            f.Tick(.20f, fire: true);
            Assert.That(f.Equipped.LoadedAmmo, Is.EqualTo(2), "The recovery pose must finish before firing.");
            view.SetPose(false, 0f, false, false, .10f);
            Assert.That(view.transform.Find("Medicine").gameObject.activeSelf, Is.False);
            f.Tick(.20f, fire: true);
            Assert.That(f.Equipped.LoadedAmmo, Is.EqualTo(1));
        }

        [TestCase("switch")]
        [TestCase("controls")]
        [TestCase("stack")]
        public void InterruptedTreatmentCannotHealOrConsumeAnotherStack(string reason)
        {
            var f = new Fight(this, 2, 0);
            Assert.That(f.Bag.TryAdd("medkit", 2, out _), Is.True);
            string chosen = f.Bag.Items[f.Bag.Items.Count - 1].InstanceId;
            f.Actor.Health.Damage(30f, null);
            f.Session.UseMedicine(chosen);
            Assert.That(f.Actor.IsTreating, Is.True);
            if (reason == "switch") Set(f.Session.Input, "SwitchWeaponPressed", true);
            if (reason == "controls") f.Session.Input.Gameplay = false;
            if (reason == "stack") f.Bag.Remove(chosen);
            Invoke(f.Actor, "TickTreatment", 2f);
            Assert.That(f.Actor.IsTreating, Is.False);
            Assert.That(f.Actor.Health.Current, Is.EqualTo(70f));
            Assert.That(f.Bag.Count("medkit"), Is.EqualTo(reason == "stack" ? 1 : 2));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SwitchingOnReloadCompletionFrameDoesNotSpendAmmo(bool manual)
        {
            int loaded = manual ? 2 : 0;
            var f = new Fight(this, loaded, 7);
            f.Tick(0.25f, reload: manual);
            var pending = f.Weapon.Magazine;
            Assert.That(pending.Reloading, Is.True);

            f.Tick(1.1f, swap: true);
            Assert.That(f.Weapon.ActiveIsBound, Is.True);
            Assert.That(f.Actor.WeaponView.Form, Is.EqualTo(WeaponForm.Tangdao));
            Assert.That(f.Weapon.Magazine, Is.Null);
            Assert.That(pending.Reloading, Is.False);
            Assert.That(f.Equipped.LoadedAmmo, Is.EqualTo(loaded));
            Assert.That(f.Bag.Count("ammo"), Is.EqualTo(7));
            f.Tick(0.05f, swap: true);
            Assert.That(f.Weapon.ActiveIsBound, Is.False);
            Assert.That(f.Weapon.Magazine.Rounds, Is.EqualTo(loaded));
            Assert.That(f.Weapon.Magazine.Reloading, Is.False);
            Assert.That(f.Bag.Count("ammo"), Is.EqualTo(7));
        }

        [Test]
        public void ReloadAndFireWriteToReplacementInventoryInstanceAndApplyItsMods()
        {
            var f = new Fight(this, 0, 4);
            f.Tick(0.25f);
            Assert.That(f.Weapon.Magazine.Reloading, Is.True);
            var old = f.Equipped;
            var snapshot = f.Bag.Snapshot();
            var mods = new WeaponMods { Sight = 2, OwnedSight = 2, Grip = 1, OwnedGrip = 1, Stock = 1, OwnedStock = 1 };
            snapshot.Find(item => item.InstanceId == old.InstanceId).Mods = mods;
            f.Bag.Remove(old.InstanceId);
            f.Bag.Restore(snapshot);
            Assert.That(f.Equipped, Is.Not.SameAs(old));
            var expected = WeaponTuning.Evaluate(f.Rifle, mods);

            f.Tick(1.1f);
            Assert.That(f.Weapon.AimFov, Is.EqualTo(expected.AimFov));
            Assert.That(f.Weapon.AimSeconds, Is.EqualTo(expected.AimSeconds));
            Assert.That(f.Equipped.LoadedAmmo, Is.EqualTo(4));
            Assert.That(f.Weapon.Reserve, Is.Zero);
            f.Tick(0.2f, fire: true);
            Assert.That(f.Equipped.LoadedAmmo, Is.EqualTo(3));
            Assert.That(f.Weapon.Magazine.Rounds, Is.EqualTo(3));
            Assert.That(old.LoadedAmmo, Is.Zero);
        }

        [Test]
        public void BoundBladeIsTheFallbackAndTracksTheChosenFormWithoutEnteringInventory()
        {
            var f = new Fight(this, 0, 0, withRifle: false);
            Assert.That(f.Weapon.ActiveIsBound, Is.True);
            Assert.That(f.Weapon.IsMelee, Is.True);
            Assert.That(f.Weapon.Definition.Form, Is.EqualTo(WeaponForm.Tangdao));
            f.Session.Profile.BoundBladeId = BladeCatalog.ShortBladeId;
            f.Tick(0.25f, swap: true);
            Assert.That(f.Weapon.ActiveIsBound, Is.True);
            Assert.That(f.Actor.WeaponView.Form, Is.EqualTo(WeaponForm.ShortBlade));
            Assert.That(f.Weapon.Magazine, Is.Null);
            Assert.That(f.Bag.Items, Is.Empty);
        }

        [Test]
        public void MeleeChainsThreeSingleContactsAndDamageInterruptsTheNextContact()
        {
            var f = new Fight(this, 0, 0, withRifle: false);
            var targetObject = AddObject("Melee target");
            targetObject.transform.position = f.Actor.View.transform.position + Vector3.forward * 1.28f;
            targetObject.AddComponent<BoxCollider>().size = new Vector3(0.6f, 0.75f, 0.4f);
            var target = targetObject.AddComponent<Health>();
            target.Initialize(250, 1);
            Physics.SyncTransforms();
            int contacts = 0;
            target.Damaged += (direction, lost) => contacts++;

            f.Tick(0.24f, fire: true);
            for (int i = 0; i < 3; i++)
            {
                Assert.That(Field<int>(f.Weapon, "contactStep"), Is.EqualTo(i));
                f.Tick(0.18f);
                Assert.That(contacts, Is.EqualTo(i));
                f.Tick(0.16f);
                Assert.That(contacts, Is.EqualTo(i + 1));
                f.Tick(0.08f);
                Assert.That(contacts, Is.EqualTo(i + 1));
                f.Tick(0.25f, fire: i < 2);
            }
            f.Tick(0.04f, fire: true);
            Assert.That(Field<float>(f.Weapon, "contactAt"), Is.GreaterThan(0));
            f.Actor.Health.Damage(1, target);
            f.Tick(0.45f);
            Assert.That(contacts, Is.EqualTo(3));
            Assert.That(target.Current, Is.EqualTo(250 - 3 * f.Weapon.Definition.Damage));
        }

        [Test]
        public void WeaponFormsShareOneSleeveRigAndAllThreeSightsReachTheViewAxis()
        {
            var actor = AddObject("Viewmodel test");
            actor.transform.SetPositionAndRotation(TestOrigin, Quaternion.Euler(17, 46, -11));
            var camera = actor.AddComponent<Camera>();
            camera.enabled = false;
            camera.nearClipPlane = 0.09f;
            var view = WeaponVisual.Create(camera.transform, true);
            var meshes = new HashSet<Mesh>();
            float[] sightHeights = { 0.132f, 0.148f, 0.164f };
            for (int level = 0; level < sightHeights.Length; level++)
            {
                view.SetSightLevel(level);
                for (int i = 0; i < 22; i++) view.SetPose(true, 3.5f, false, true, 0.016f);
                Mesh mesh = view.GetComponent<MeshFilter>().sharedMesh;
                Assert.That(meshes.Add(mesh), Is.True);
                Assert.That(view.GetComponent<MeshRenderer>().enabled, Is.True);
                Assert.That(mesh.subMeshCount, Is.EqualTo(1));
                Assert.That(view.Muzzle.localPosition.z, Is.GreaterThan(mesh.bounds.max.z));
                AssertCentered(camera, view.transform.TransformPoint(new Vector3(0, sightHeights[level], -0.16f)));
                AssertCentered(camera, view.transform.TransformPoint(new Vector3(0, sightHeights[level], 0.29f)));
            }

            var sleeves = view.GetComponentInChildren<SkinnedMeshRenderer>();
            Assert.That(sleeves, Is.Not.Null);
            Assert.That(sleeves.shadowCastingMode, Is.EqualTo(ShadowCastingMode.Off));
            foreach (WeaponForm form in new[] { WeaponForm.Baton, WeaponForm.Tangdao, WeaponForm.ShortBlade })
            {
                view.SetForm(form);
                Assert.That(view.IsMelee, Is.True);
                Assert.That(meshes.Add(view.GetComponent<MeshFilter>().sharedMesh), Is.True);
                Assert.That(view.GetComponentInChildren<SkinnedMeshRenderer>(), Is.SameAs(sleeves));
                Assert.That(Vector3.Distance(view.LeftGrip, view.RightGrip), Is.GreaterThan(0.05f));
            }
            Assert.That(view.GetComponentsInChildren<Collider>(true), Is.Empty);
            view.SetEquipped(false);
            Assert.That(sleeves.gameObject.activeInHierarchy, Is.False);
        }

        [TestCase(WeaponForm.Rifle)]
        [TestCase(WeaponForm.Tangdao)]
        [TestCase(WeaponForm.ShortBlade)]
        [TestCase(WeaponForm.Baton)]
        public void ForegroundWeaponAndSleevesKeepTheirPoseAtSolidCover(WeaponForm form)
        {
            var fight = new Fight(this, 4, 0);
            var eye = fight.Actor.View.transform;
            var view = fight.Actor.WeaponView;
            view.SetForm(form);
            view.SetPose(false, 0f, false, false, 0.016f);
            Vector3 before = view.transform.localPosition;
            Quaternion rotation = view.transform.localRotation;
            AddBox(eye.position + eye.forward * 0.58f, new Vector3(3f, 2f, 0.08f));
            Physics.SyncTransforms();
            view.SetPose(false, 0f, false, false, 0.016f);

            Assert.That(view.Muzzle.gameObject.layer, Is.EqualTo(FirstPersonView.Layer));
            foreach (var renderer in view.GetComponentsInChildren<Renderer>())
            {
                Assert.That(renderer.enabled, Is.True);
                Assert.That(renderer.gameObject.layer, Is.EqualTo(FirstPersonView.Layer));
                Assert.That(renderer.shadowCastingMode, Is.EqualTo(ShadowCastingMode.Off));
            }
            Assert.That(Vector3.Distance(view.transform.localPosition, before), Is.LessThan(0.001f));
            Assert.That(Quaternion.Angle(view.transform.localRotation, rotation), Is.LessThan(0.01f));
            Assert.That(view.GetComponentsInChildren<Collider>(), Is.Empty);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void WorldWallBlocksDamageEvenWhenTheEquippedModelDrawsOnTop(bool blade)
        {
            var fight = new Fight(this, 4, 0, withRifle: !blade);
            var eye = fight.Actor.View.transform;
            var wall = AddBox(eye.position + eye.forward * 0.55f, new Vector3(3f, 2f, 0.08f));
            var behind = AddBox(eye.position + eye.forward * 1.35f, new Vector3(0.7f, 1f, 0.22f));
            var target = behind.gameObject.AddComponent<Health>();
            target.Initialize(100f, 1);
            Physics.SyncTransforms();

            fight.Tick(0.3f, fire: true);
            fight.Tick(0.34f);
            Assert.That(target.Current, Is.EqualTo(100f));
            Assert.That(fight.Actor.WeaponView.GetComponent<MeshRenderer>().enabled, Is.True);
            if (!blade) Assert.That(fight.Equipped.LoadedAmmo, Is.EqualTo(3));
            wall.enabled = false;
            Physics.SyncTransforms();
            fight.Tick(0.4f, fire: true);
            fight.Tick(0.34f);
            Assert.That(target.Current, Is.LessThan(100f));
        }

        [Test]
        public void ShotAlignsTheViewmodelMuzzleWithItsTravelWithoutMovingTheSocket()
        {
            var fight = new Fight(this, 4, 0);
            var eye = fight.Actor.View.transform;
            var visual = fight.Actor.WeaponView;
            eye.rotation = Quaternion.Euler(-12f, 30f, 6f);
            for (int i = 0; i < 24; i++) visual.SetPose(false, 6.5f, true, false, 0.016f);
            visual.SetPose(false, 3.8f, false, false, 0.016f, true);
            Vector3 socket = visual.Muzzle.position;
            Vector3 end = eye.position + eye.forward * 18f;
            Invoke(visual, "AlignShot", eye.forward, end);
            Assert.That(Vector3.Distance(visual.Muzzle.position, socket), Is.LessThan(0.001f));
            Assert.That(Vector3.Angle(visual.Muzzle.forward, end - socket), Is.LessThan(0.15f));
            Invoke(visual, "AlignShot", eye.forward, eye.position + eye.forward * 0.45f);
            Assert.That(Vector3.Angle(visual.Muzzle.forward, eye.forward), Is.LessThan(0.15f));
        }

        [Test]
        public void TriggerDuringSprintWalksAndFiresOnThatFrameThenResumesSprint()
        {
            var fight = new Fight(this, 4, 0);
            var actor = fight.Actor;
            actor.gameObject.layer = 2;
            var motor = actor.gameObject.AddComponent<CharacterController>();
            motor.height = 1.8f;
            motor.center = Vector3.up * 0.9f;
            motor.radius = 0.35f;
            typeof(PlayerController).GetField("motor", InstancePrivate).SetValue(actor, motor);
            typeof(PlayerController).GetField("session", InstancePrivate).SetValue(actor, fight.Session);
            float sprintSpeed = 6.5f * Mathf.Lerp(1f, 0.65f, fight.Bag.Weight / fight.Bag.MaxWeight);
            float walkSpeed = 3.8f * Mathf.Lerp(1f, 0.65f, fight.Bag.Weight / fight.Bag.MaxWeight);

            fight.Tick(0.03f, move: true);
            Assert.That(actor.Sprinting, Is.True);
            Assert.That(actor.MoveSpeed, Is.EqualTo(sprintSpeed).Within(0.03f));
            fight.Tick(0.2f, fire: true, move: true);
            Assert.That(actor.Sprinting, Is.False);
            Assert.That(actor.MoveSpeed, Is.EqualTo(walkSpeed).Within(0.03f));
            Assert.That(fight.Equipped.LoadedAmmo, Is.EqualTo(3));
            Assert.That(Field<float>(actor.WeaponView, "run"), Is.Zero);
            fight.Tick(0.04f, move: true);
            Assert.That(actor.Sprinting, Is.False);
            fight.Tick(0.18f, move: true);
            Assert.That(actor.Sprinting, Is.True);
        }

        [TestCase(-1f)]
        [TestCase(1f)]
        public void LeanClampsTheCameraBeforeTheNearSideOfADoorJamb(float side)
        {
            var fight = new Fight(this, 4, 0);
            var actor = fight.Actor;
            var jamb = AddBox(actor.transform.position + new Vector3(0.29f * side, 1.5f, 0f), new Vector3(0.04f, 2f, 1f));
            Physics.SyncTransforms();
            var fit = typeof(PlayerController).GetMethod("FitLean", InstancePrivate);
            float againstJamb = (float)fit.Invoke(actor, new object[] { 1.5f, 0.22f * side });
            Assert.That(againstJamb * side, Is.InRange(0.085f, 0.10f));
            jamb.enabled = false;
            Physics.SyncTransforms();
            Assert.That(fit.Invoke(actor, new object[] { 1.5f, 0.22f * side }), Is.EqualTo(0.22f * side));
        }

        BoxCollider AddBox(Vector3 position, Vector3 size)
        {
            var item = AddObject("Viewmodel test obstacle");
            item.transform.position = position;
            var box = item.AddComponent<BoxCollider>();
            box.size = size;
            return box;
        }

        [Test]
        public void SurfaceMarkFollowsMovingDoorAndPoolExpiresWithoutMarkingActors()
        {
            var camera = AddObject("Mark observer").AddComponent<Camera>();
            camera.transform.position = TestOrigin;
            var effects = AddObject("Combat effects").AddComponent<RaidCombatEffects>();
            effects.Initialize();
            effects.GetType().GetField("eye", InstancePrivate).SetValue(effects, camera);
            var door = AddObject("Door leaf");
            door.transform.position = TestOrigin + Vector3.forward * 3f;
            var box = door.AddComponent<BoxCollider>();
            box.size = new Vector3(1.8f, 2.2f, 0.25f);
            Physics.SyncTransforms();
            var muzzle = TestOrigin + Vector3.forward * 80f;
            var panelRay = new Ray(TestOrigin + Vector3.right * 0.33f, Vector3.forward);
            Assert.That(box.Raycast(panelRay, out var contact, 5f), Is.True);
            effects.Shot(muzzle, contact.point, contact.normal, true, false, false);
            Assert.That(Field<int>(effects, "liveMarks"), Is.EqualTo(1));
            var meshFilter = effects.GetComponentInChildren<MeshFilter>();
            var renderer = meshFilter.GetComponent<MeshRenderer>();
            float now = Time.time;
            Invoke(effects, "Draw", now + 1f);
            Assert.That(meshFilter.sharedMesh.GetIndexCount(0), Is.EqualTo(6));
            Vector3 center = meshFilter.transform.TransformPoint(meshFilter.sharedMesh.bounds.center);
            Assert.That(Vector3.Distance(center, contact.point), Is.LessThan(0.01f));
            Vector3 relative = door.transform.InverseTransformPoint(center);
            door.transform.SetPositionAndRotation(door.transform.position + new Vector3(0.75f, 0, 0.2f), Quaternion.Euler(0, 52, 0));
            Invoke(effects, "Draw", now + 2f);
            center = meshFilter.transform.TransformPoint(meshFilter.sharedMesh.bounds.center);
            Assert.That(Vector3.Distance(center, door.transform.TransformPoint(relative)), Is.LessThan(0.001f));
            door.SetActive(false);
            Invoke(effects, "Draw", now + 3f);
            Assert.That(renderer.enabled, Is.False);
            door.SetActive(true);
            Invoke(effects, "Draw", now + 4f);
            Assert.That(renderer.enabled, Is.True);

            var person = AddObject("Actor with a box collider");
            person.transform.position = TestOrigin + new Vector3(-2, 0, 3);
            person.AddComponent<Health>();
            var actorCollider = person.AddComponent<BoxCollider>();
            Physics.SyncTransforms();
            Assert.That(actorCollider.Raycast(new Ray(TestOrigin + Vector3.left * 2f, Vector3.forward), out var actorHit, 5f), Is.True);
            effects.Shot(muzzle, actorHit.point, actorHit.normal, true, false, false);
            Assert.That(Field<int>(effects, "liveMarks"), Is.EqualTo(1));

            int capacity = Field<System.Array>(effects, "marks").Length;
            Vector3 point = door.transform.TransformPoint(new Vector3(0, 0, -box.size.z * 0.5f));
            for (int i = 0; i < capacity + 3; i++) effects.Shot(muzzle, point, -door.transform.forward, true, false, false);
            Assert.That(Field<int>(effects, "liveMarks"), Is.EqualTo(capacity));
            Invoke(effects, "Draw", now + 20f);
            Assert.That(Field<int>(effects, "liveMarks"), Is.Zero);
            Assert.That(renderer.enabled, Is.False);
        }

        static void AssertCentered(Camera camera, Vector3 point)
        {
            Vector3 projection = camera.WorldToViewportPoint(point);
            Assert.That(projection.x, Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(projection.y, Is.EqualTo(0.5f).Within(0.001f));
        }

        GameObject AddObject(string name)
        {
            var item = new GameObject(name);
            created.Add(item);
            return item;
        }

        T AddAsset<T>() where T : ScriptableObject
        {
            var item = ScriptableObject.CreateInstance<T>();
            created.Add(item);
            return item;
        }

        static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, InstancePrivate).GetValue(owner);
        static void Invoke(object owner, string name, params object[] args) => owner.GetType().GetMethod(name, InstancePrivate).Invoke(owner, args);
        static void Set(object owner, string name, object value) => owner.GetType().GetProperty(name).GetSetMethod(true).Invoke(owner, new[] { value });

        sealed class Fight
        {
            public readonly GameSession Session;
            public readonly GridInventory Bag;
            public readonly PlayerController Actor;
            public readonly WeaponController Weapon;
            public readonly WeaponDefinition Rifle;
            public ItemStack Equipped => Bag.Equipped(EquipmentSlot.Weapon);
            readonly PlayerInputReader input;
            float now;

            public Fight(WeaponCombatRegressionTests test, int loaded, int reserve, bool withRifle = true)
            {
                Rifle = test.AddAsset<WeaponDefinition>();
                Rifle.Form = WeaponForm.Rifle;
                Rifle.MagazineSize = 8;
                Rifle.AmmoId = "ammo";
                Rifle.ReloadSeconds = 1;
                var rifleItem = test.AddAsset<ItemDefinition>();
                rifleItem.Id = WeaponDefinition.RifleId;
                rifleItem.Kind = ItemKind.Weapon;
                rifleItem.Weapon = Rifle;
                var ammo = test.AddAsset<ItemDefinition>();
                ammo.Id = Rifle.AmmoId;
                ammo.Kind = ItemKind.Ammo;
                ammo.MaxStack = 20;
                var medicine = test.AddAsset<ItemDefinition>();
                medicine.Id = "medkit";
                medicine.Kind = ItemKind.Medicine;
                medicine.UseSeconds = 1f;
                medicine.HealAmount = 30f;
                var items = test.AddAsset<ItemCatalog>();
                items.Items.Add(rifleItem);
                items.Items.Add(ammo);
                items.Items.Add(medicine);
                Bag = new GridInventory(4, 4, 100, items);
                if (withRifle)
                {
                    Assert.That(Bag.TryAdd(rifleItem.Id, 1, out var giveError), Is.True, giveError);
                    Assert.That(Bag.TryEquip(Bag.Items[0].InstanceId, EquipmentSlot.Weapon, out var equipError), Is.True, equipError);
                    Equipped.LoadedAmmo = loaded;
                }
                if (reserve > 0) Assert.That(Bag.TryAdd(ammo.Id, reserve, out var ammoError), Is.True, ammoError);

                var blades = test.AddAsset<BladeCatalog>();
                blades.Blades = new[] {
                    Blade(test, BladeCatalog.DefaultId, WeaponForm.Tangdao), Blade(test, BladeCatalog.ShortBladeId, WeaponForm.ShortBlade)
                };
                var sessionNode = test.AddObject("Combat fixture session");
                sessionNode.SetActive(false);
                input = sessionNode.AddComponent<PlayerInputReader>();
                input.Gameplay = true;
                Session = sessionNode.AddComponent<GameSession>();
                Set(Session, "Phase", SessionPhase.Raid);
                Set(Session, "Input", input);
                Set(Session, "Catalog", items);
                Set(Session, "Loadout", Bag);
                Set(Session, "Profile", new ProfileData());
                Set(Session, "Blades", blades);

                var actor = test.AddObject("Combat fixture player");
                actor.transform.position = TestOrigin;
                Actor = actor.AddComponent<PlayerController>();
                Actor.enabled = false;
                Set(Session, "Player", Actor);
                typeof(PlayerController).GetField("session", InstancePrivate).SetValue(Actor, Session);
                var health = actor.AddComponent<Health>();
                health.Initialize(100, 0);
                Set(Actor, "Health", health);
                var body = OperatorVisual.Create(actor.transform, OperatorStyle.Female, 0, 2);
                body.ShadowsOnly();
                Set(Actor, "Operator", body);
                var camera = new GameObject("Combat test view", typeof(Camera)).GetComponent<Camera>();
                camera.transform.SetParent(actor.transform, false);
                camera.enabled = false;
                camera.nearClipPlane = 0.09f;
                Set(Actor, "View", camera);
                Weapon = actor.AddComponent<WeaponController>();
                Weapon.enabled = false;
                Set(Actor, "Weapon", Weapon);
                Weapon.Initialize(Session, Actor);
                now = Time.time;
            }

            public void Tick(float delta, bool fire = false, bool swap = false, bool reload = false, bool move = false)
            {
                Set(input, "Fire", fire);
                Set(input, "FirePressed", fire);
                Set(input, "SwitchWeaponPressed", swap);
                Set(input, "ReloadPressed", reload);
                now += delta;
                if (move)
                {
                    Set(input, "Sprint", true);
                    Set(input, "Move", Vector2.up);
                    Invoke(Actor, "Tick", now, 0.016f);
                }
                Invoke(Weapon, "Tick", now, delta);
            }

            static BladeChoice Blade(WeaponCombatRegressionTests test, string id, WeaponForm form)
            {
                var definition = test.AddAsset<WeaponDefinition>();
                definition.Form = form;
                definition.Melee = true;
                definition.FireInterval = 0.65f;
                definition.Range = 2f;
                definition.Damage = 28f;
                return new BladeChoice { Id = id, Weapon = definition };
            }
        }
    }
}
