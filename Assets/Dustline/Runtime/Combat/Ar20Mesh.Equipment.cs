using UnityEngine;

namespace Dustline
{
    sealed partial class Ar20Mesh
    {
        public static bool IsBlade(WeaponForm form) => form == WeaponForm.Baton || form == WeaponForm.Tangdao || form == WeaponForm.ShortBlade;

        public static float Front(WeaponForm form) => form switch
        {
            WeaponForm.Pistol => .223f, WeaponForm.Smg => .365f, WeaponForm.Shotgun => .640f,
            WeaponForm.Marksman => .620f, WeaponForm.Sniper => .754f, WeaponForm.Lmg => .563f, _ => Muzzle.z
        };

        public static Vector3 MagazineSeat(WeaponForm form) => form switch
        {
            WeaponForm.Pistol => new Vector3(0f, -.007f, -.164f),
            WeaponForm.Smg => new Vector3(0f, 0f, -.033f),
            WeaponForm.Lmg => new Vector3(0f, -.016f, .035f),
            _ => Vector3.zero
        };

        public Mesh PartMesh(PartSlot slot, int style)
        {
            int key = 8 * (int)slot + Mathf.Clamp(style, 0, 7);
            if (parts.TryGetValue(key, out var model)) return model;
            var b = new Sculpt();
            switch (slot)
            {
                case PartSlot.Sight:
                    BuildSights(b, Mathf.Clamp(style, 1, 4));
                    break;
                case PartSlot.Grip:
                    var at = new Vector3(0, -.024f, -.14f);
                    bool slim = (style & 1) != 0;
                    b.Loft(slim ? Finish.Polymer : Finish.Wrap, at, Quaternion.Euler(90, 0, 0),
                        S(0, 0, .025f, .032f, .007f), S(.037f, -.008f, slim ? .026f : .034f, .041f, .008f),
                        S(.119f, -.022f, slim ? .026f : .036f, .035f, .008f), S(slim ? .149f : .164f, -.025f, slim ? .026f : .037f, .033f, .009f));
                    for (int i = 0; i < 4; i++) b.Box(Finish.Deep, 0, -.08f - .018f * i, -.102f + .005f * i, slim ? .050f : .067f, .009f, .016f);
                    if (!slim) b.Box(Finish.Jade, 0f, -.177f, -.115f, .075f, .024f, .070f);
                    if (style >= 3) b.Box(Finish.Blue, 0f, -.081f, -.142f, .071f, .026f, .059f);
                    break;
                case PartSlot.Stock:
                    if ((style & 1) != 0)
                    {
                        b.Box(Finish.Steel, 0, .05f, -.326f, .058f, .027f, .21f);
                        b.Box(Finish.Graphite, 0, -.036f, -.348f, .042f, .024f, .136f);
                        b.Box(Finish.Rubber, 0, -.003f, -.438f, .079f, .158f, .036f);
                    }
                    else
                    {
                        b.Loft(Finish.Coating, S(-.473f, -.014f, .042f, .081f, .017f),
                            S(-.443f, -.006f, .05f, .086f, .019f), S(-.344f, .007f, .047f, .066f, .014f), S(-.230f, .035f, .032f, .028f, .008f));
                        b.Box(Finish.Rubber, 0, -.015f, -.475f, .082f, .162f, .025f);
                        b.Box(Finish.Jade, 0, .073f, -.374f, .077f, .026f, .151f);
                    }
                    if (style >= 3) b.Box(Finish.Blue, 0f, .067f, -.398f, .075f, .032f, .089f);
                    break;
                case PartSlot.Muzzle:
                    b.Loft(style == 2 ? Finish.Coating : Finish.Machined, S(-.026f, 0, .022f, .023f, .007f),
                        S(.002f, 0, .038f, .038f, .009f), S(style == 2 ? .13f : .072f, 0, .034f, .035f, .009f));
                    for (int side = -1; side <= 1; side += 2)
                        b.Box(Finish.Recess, side * .034f, 0, .030f, .009f, .032f, .039f);
                    break;
                case PartSlot.Magazine:
                    if (style == 3)
                    {
                        b.Box(Finish.Steel, 0f, -.096f, .045f, .048f, .147f, .057f);
                        b.Box(Finish.Polymer, 0f, -.170f, .041f, .053f, .055f, .062f);
                        b.Box(Finish.Rubber, 0f, -.204f, .048f, .060f, .026f, .071f);
                    }
                    else if (style == 2)
                    {
                        b.Box(Finish.Machined, 0, -.054f, .050f, .055f, .058f, .073f);
                        b.Box(Finish.Coating, 0, -.145f, .075f, .102f, .153f, .130f);
                        b.Box(Finish.Polymer, 0, -.218f, .075f, .112f, .025f, .143f);
                        for (int i = 0; i < 5; i++) b.Box(Finish.Steel, 0f, -.136f, .024f + i * .026f, .108f, .103f, .008f);
                    }
                    else
                    {
                        AddMagazine(b);
                        b.Box(Finish.Rubber, 0, -.232f, .086f, .055f, .054f, .086f);
                        b.Box(Finish.Steel, -.028f, -.176f, .087f, .009f, .071f, .043f);
                    }
                    break;
            }
            model = b.FinishMesh();
            model.name = "Dustline / part " + slot + "/" + style;
            return parts[key] = model;
        }

        public Mesh ObjectMesh(ItemDefinition item)
        {
            if (item.Kind == ItemKind.Medicine) return MedicineMesh(item.MedicineVisual);
            if (item.Kind == ItemKind.Part) return PartMesh(item.Part.Slot, WeaponRules.PartLook(item));
            if (objects.TryGetValue(item.Id, out var mesh)) return mesh;
            var b = new Sculpt();
            float tier = Mathf.Clamp(item.Tier, 1, 6);
            switch (item.Kind)
            {
                case ItemKind.Ammo:
                    Finish shell = item.AmmoFamily == AmmoFamily.Silver ? Finish.Steel : item.AmmoFamily == AmmoFamily.Slate ? Finish.Wrap
                        : item.AmmoFamily == AmmoFamily.Copper ? Finish.Amber : Finish.Coating;
                    b.Box(item.Id == "ammo_amber_range" ? Finish.Jade : Finish.Warm, 0f, -.017f, 0f, .112f, .031f, .082f);
                    for (int i = -1; i <= 1; i++)
                        b.Loft(shell, new Vector3(i * .029f, .009f, 0f), Quaternion.identity,
                            S(-.034f, 0f, .010f, .010f, .004f), S(.024f, 0f, .010f, .010f, .004f), S(.039f, 0f, .005f, .005f, .002f));
                    break;
                case ItemKind.Helmet:
                    b.Loft(Finish.Coating, S(-.138f,.011f,.088f,.075f,.037f), S(-.092f,.015f,.120f,.109f,.044f),
                        S(.056f,.015f,.123f,.105f,.045f), S(.135f,.006f,.097f,.066f,.031f));
                    b.Box(Finish.Deep, 0, -.052f, .082f, .219f, .023f + .005f * tier, .130f);
                    for (int side = -1; side <= 1; side += 2)
                        b.Box(Finish.Graphite, side * .115f, -.036f, -.016f, .027f, .065f + .004f * tier, .096f);
                    break;
                case ItemKind.Armor:
                case ItemKind.Rig:
                    bool rig = item.Kind == ItemKind.Rig;
                    b.Box(Finish.Coating, 0f, 0f, 0, rig ? .30f : .352f, rig ? .168f : .318f, .082f + tier * .006f);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        if (!rig) b.Box(Finish.Graphite, side * .119f, .156f, -.004f, .072f, .105f, .121f);
                        b.Box(Finish.Wrap, side * .081f, -.024f, .055f, rig ? .069f + tier * .009f : .136f, .108f, .069f);
                        b.Box(Finish.Machined, side * .129f, -.044f, .093f, .036f, .034f, .013f);
                    }
                    if (item.Coverage.HasFlag(BodyCoverage.Abdomen)) b.Box(Finish.Jade, 0f, -.172f, .023f, .207f, .126f, .072f);
                    if (item.Coverage.HasFlag(BodyCoverage.LeftArm))
                        for (int side = -1; side <= 1; side += 2) b.Box(Finish.Coating, side * .189f,.095f,.002f,.066f,.114f,.112f);
                    break;
                case ItemKind.Pack:
                    float width = .247f + item.Storage.Columns * .009f;
                    float height = .237f + item.Storage.Rows * .018f;
                    float depth = .116f + tier * .012f;
                    b.Loft(Finish.Coating, S(-depth * .55f,0,width * .43f,height * .44f,.035f),
                        S(-depth * .40f,0,width * .5f,height * .5f,.036f), S(depth * .43f,0,width * .5f,height * .48f,.035f),
                        S(depth * .58f,0,width * .42f,height * .41f,.034f));
                    b.Box(Finish.Jade, 0f, -.078f, depth * .53f, width * .75f, height * .41f, .050f);
                    for (int side = -1; side <= 1; side += 2)
                        b.Box(Finish.Wrap, side * width * .29f, .007f, -depth * .59f, .037f, height * .87f, .026f);
                    b.Box(Finish.Graphite, 0, height * .49f, -.008f, .088f, .036f, .032f);
                    break;
                case ItemKind.Valuable:
                    if (item.Id == "circuit")
                    {
                        b.Box(Finish.Jade, 0, 0, 0, .102f, .008f, .181f);
                        for (int i = 0; i < 5; i++) b.Box(i % 2 == 0 ? Finish.Graphite : Finish.Steel, i % 2 * .035f - .022f, .013f, -.063f + .032f * i, .036f, .019f, .024f);
                        b.Box(Finish.Amber, 0, .003f, .081f, .092f, .006f, .013f);
                    }
                    else
                    {
                        b.Loft(Finish.Blue, S(-.092f, .01f, .064f, .046f, .019f), S(-.062f,.014f,.080f,.053f,.020f),
                            S(.061f,.014f,.080f,.053f,.020f), S(.088f,.01f,.064f,.046f,.020f));
                        b.Box(Finish.Graphite, 0,.062f,.028f,.109f,.015f,.071f);
                        b.Box(Finish.Signal, 0,.071f,.028f,.047f,.005f,.036f);
                    }
                    break;
                default:
                    b.Box(Finish.Steel, 0, 0, 0, .15f, .10f, .15f);
                    break;
            }
            mesh = b.FinishMesh();
            mesh.name = "Dustline / item " + item.Id;
            return objects[item.Id] = mesh;
        }

        static Mesh BuildFrame(WeaponForm form, int sight, bool customGrip, bool customStock)
        {
            var b = new Sculpt();
            bool pistol = form == WeaponForm.Pistol, shortBody = form == WeaponForm.Smg,
                smooth = form == WeaponForm.Shotgun, longBody = form == WeaponForm.Sniper, support = form == WeaponForm.Lmg;
            float nose = Front(form);
            float receiverEnd = pistol ? nose - .024f : shortBody ? .231f : smooth ? .331f : support ? .350f : .393f;
            float width = pistol ? .027f : .038f + (support ? .016f : 0f);
            Finish frame = smooth ? Finish.Wrap : longBody ? Finish.Blue : form == WeaponForm.Marksman ? Finish.Coating
                : support ? Finish.Polymer : Finish.Anodized;
            b.Loft(frame, S(pistol ? -.165f : -.248f, .038f, width * .83f, .036f, .008f),
                S(-.132f, .037f, width, .048f, .010f), S(receiverEnd * .50f, .037f, width, .045f, .008f),
                S(receiverEnd, .029f, width * .7f, .029f, .007f));
            b.Loft(Finish.Graphite, S(receiverEnd - .039f, .032f, width * .56f, .021f, .006f),
                S(nose - .011f, .032f, .015f, .015f, .005f));
            if (smooth) b.Loft(Finish.Steel, S(.109f, -.019f, .033f, .011f, .004f), S(nose - .057f, -.019f, .018f, .012f, .006f));
            if (!pistol && !customStock)
            {
                float tail = support ? -.438f : longBody ? -.457f : shortBody ? -.342f : -.412f;
                b.Loft(frame, S(tail, -.008f, .038f, .067f, .011f), S(tail + .086f, -.004f, .042f, .059f, .011f),
                    S(-.242f, .037f, .028f, .028f, .008f));
                b.Box(Finish.Rubber, 0, -.007f, tail, .079f, .143f, .022f);
            }
            if (!customGrip)
                b.Loft(Finish.Polymer, new Vector3(0, -.018f, -.14f), Quaternion.Euler(90, 0, 0),
                    S(0, 0, .028f, .038f, .008f), S(.092f, -.017f, .027f, .034f, .009f), S(.143f, -.032f, .031f, .034f, .008f));
            b.Box(Finish.Graphite, 0, -.048f, -.056f, .048f, .029f, .093f);
            b.Box(Finish.Recess, 0, -.064f, -.057f, .030f, .025f, .068f);
            int trim = pistol ? 5 : support ? 12 : 8;
            for (int i = 0; i < trim; i++)
            {
                float z = Mathf.Lerp(pistol ? -.14f : -.18f, pistol ? .116f : receiverEnd * .9f, i / (float)(trim - 1));
                b.Box(Finish.Parkerized, 0f, .087f, z, width * 1.77f, .007f, .017f);
                b.Box(Finish.Recess, width, .053f, z, .006f, .022f, .012f);
            }
            if (support)
                b.Box(Finish.Ink, 0f, .119f, .154f, .049f, .049f, .212f);
            if (sight == 0)
            {
                float rail = pistol ? .118f : .132f;
                b.Window(Finish.Graphite, Finish.Recess, -.163f, -.143f, rail, .022f, .020f, .014f, .012f);
                b.Box(Finish.Machined, 0, rail - .013f, Mathf.Min(nose - .026f, .31f), .011f, .026f, .014f);
            }
            return b.FinishMesh();
        }
    }
}
