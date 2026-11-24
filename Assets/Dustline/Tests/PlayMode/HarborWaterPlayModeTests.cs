using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Dustline.Tests
{
    public sealed class HarborWaterPlayModeTests
    {
        const float Sea = -1.4f, Height = 1.8f, Frame = .02f;

        [UnityTest]
        public IEnumerator AControllerCanWadeSwimAndClimbOutOnEitherBank()
        {
            using (var built = new PortWorldFixture())
            {
                const float x = -137f, channel = -35f, bank = 25.3f;
                Vector3 south = PortWorldFixture.Floor(x, channel - bank);
                Vector3 north = PortWorldFixture.Floor(x, channel + bank);
                var nav = built.World.Navigation;
                Assert.That(nav.CanStandAt(south) && nav.CanStandAt(north), Is.True);
                Assert.That(nav.CanTraverse(south, north), Is.False, "A swimmer can cross, but dry navigation cannot.");

                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 start = side < 0 ? north : south;
                    Vector3 finish = side < 0 ? south : north;
                    var motor = Probe(built.World.transform, start + Vector3.up * .06f);
                    yield return new WaitForFixedUpdate();

                    var water = new HarborSwimming();
                    float velocity = 0;
                    bool enteredWading = false, swam = false, exitedWading = false;
                    for (int i = 0; i < 1300 && (finish.z - motor.transform.position.z) * side > 0; i++)
                    {
                        Step(motor, ref water, ref velocity, Vector3.forward * side);
                        if (water.IsWading && !swam) enteredWading = true;
                        if (water.IsSwimming) swam = true;
                        if (water.IsWading && swam) exitedWading = true;
                        if (i % 64 == 0) yield return null;
                    }
                    Assert.That(enteredWading && swam && exitedWading, Is.True, $"Crossing toward bank {side}");
                    Assert.That(motor.transform.position.z, Is.EqualTo(finish.z).Within(.16f));
                    Assert.That(motor.transform.position.y, Is.EqualTo(finish.y).Within(.17f));
                    Assert.That(water.Mode, Is.EqualTo(HarborWaterMode.Dry));
                    motor.gameObject.SetActive(false);
                    Object.Destroy(motor.gameObject);
                }
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator BridgeAndQuayKeepSupportingCollidersWithoutBlockingTheWaterway()
        {
            using (var built = new PortWorldFixture())
            {
                var water = new HarborSwimming();
                Vector3 deck = PortWorldFixture.Floor(-334f, -44f);
                Assert.That(deck.y, Is.EqualTo(2.55f).Within(.06f));
                water.Update(true, deck, Height, true);
                Assert.That(water.Mode, Is.EqualTo(HarborWaterMode.Dry));

                var motor = Probe(built.World.transform, new Vector3(-344f, deck.y, -39.5f));
                yield return new WaitForFixedUpdate();
                float velocity = 0;
                water.Update(true, motor.transform.position, Height, false);
                Assert.That(water.Mode, Is.EqualTo(HarborWaterMode.Dry));
                for (int i = 0; i < 100 && !water.IsSwimming; i++)
                    Step(motor, ref water, ref velocity, Vector3.zero);
                Assert.That(water.IsSwimming, Is.True, "Falling beside the deck should enter the river.");
                for (int i = 0; i < 445; i++)
                {
                    Step(motor, ref water, ref velocity, Vector3.right);
                    Assert.That(water.IsSwimming, Is.True, $"Swimming under the bridge at x={motor.transform.position.x}");
                    if (i % 64 == 0) yield return null;
                }
                Assert.That(motor.transform.position.x, Is.GreaterThan(-324f));
                Assert.That(motor.transform.position.y + Height, Is.LessThan(deck.y - .58f));
                for (int i = 0; i < 40 && !water.IsSubmerged; i++) Step(motor, ref water, ref velocity, Vector3.zero, down: true);
                Assert.That(water.Mode, Is.EqualTo(HarborWaterMode.Submerged));
                for (int i = 0; i < 90 && water.IsSubmerged; i++) Step(motor, ref water, ref velocity, Vector3.zero, up: true);
                Assert.That(water.Mode, Is.EqualTo(HarborWaterMode.Swimming));
                motor.gameObject.SetActive(false);

                Assert.That(Cast(new Vector3(348f, Sea - .4f, -75f), Vector3.right, 13f, out _), Is.False,
                    "The river mouth should open into the basin at water height.");
                Assert.That(Cast(new Vector3(361f, Sea + .2f, -75f), Vector3.down, 8f, out var seabed), Is.True);
                Assert.That(seabed.point.y, Is.EqualTo(-5.6f).Within(.06f), "Water is visible but not a solid floor.");
                water.Update(true, new Vector3(361f, Sea - 1.25f, -75f), Height, false);
                Assert.That(water.IsSwimming, Is.True);
                Assert.That(Cast(new Vector3(362f, Sea - .3f, 165f), Vector3.left, 13f, out var quay), Is.True);
                Assert.That(quay.collider, Is.TypeOf<BoxCollider>());
                Assert.That(quay.point.x, Is.EqualTo(357f).Within(.06f));
                Assert.That(Cast(new Vector3(300f, Sea - 2f, -77f), Vector3.forward, 17f, out var canal), Is.True);
                Assert.That(canal.collider, Is.TypeOf<BoxCollider>(), "The dock-side canal wall still blocks a dive.");
                Assert.That(Cast(new Vector3(457f, Sea - 1f, 165f), Vector3.right, 16f, out var boundary), Is.True);
                Assert.That(boundary.collider, Is.TypeOf<BoxCollider>());
                Assert.That(Cast(new Vector3(-465f, Sea - 2f, -58f), Vector3.left, 9f, out _), Is.True,
                    "Diving cannot bypass the west ridge wall.");
                Assert.That(Cast(new Vector3(380f, Sea - .3f, 61f), Vector3.right, 25f, out var vessel), Is.True);
                Assert.That(vessel.collider.name, Is.EqualTo("Pilot vessel hull"));
            }
            yield return null;
        }

        static CharacterController Probe(Transform parent, Vector3 feet)
        {
            var probe = new GameObject("Swimming collision probe");
            probe.transform.SetParent(parent, false);
            probe.transform.position = feet;
            probe.layer = 2;
            var motor = probe.AddComponent<CharacterController>();
            motor.height = Height;
            motor.radius = .35f;
            motor.center = Vector3.up * (Height * .5f);
            motor.stepOffset = .25f;
            Physics.SyncTransforms();
            return motor;
        }

        static void Step(CharacterController motor, ref HarborSwimming water, ref float velocity, Vector3 direction, bool up = false, bool down = false)
        {
            Vector3 feet = motor.transform.position;
            bool grounded = motor.isGrounded;
            water.Update(true, feet, Height, grounded);
            velocity = water.IsSwimming ? water.VerticalVelocity(feet, Height, Frame, up, down, velocity)
                : (grounded && velocity < 0 ? -2f : velocity) - 20f * Frame;
            float speed = water.IsSwimming ? (water.IsSubmerged ? 2.45f : 2.8f) : water.IsWading ? 3.8f * .76f : 3.8f;
            CollisionFlags flags = motor.Move((direction * speed + Vector3.up * velocity) * Frame);
            if ((flags & CollisionFlags.Above) != 0 && velocity > 0) velocity = 0f;
            water.Update(true, motor.transform.position, Height, motor.isGrounded);
            if ((flags & CollisionFlags.Below) != 0 && velocity < 0) velocity = water.IsSwimming ? 0f : -2f;
        }

        static bool Cast(Vector3 origin, Vector3 direction, float distance, out RaycastHit hit) =>
            Physics.Raycast(origin, direction, out hit, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
    }
}
