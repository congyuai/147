using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Dustline.Tests
{
    public sealed class ItemIconRenderingTests
    {
        const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        [UnityTest]
        public IEnumerator SameBuildSharesPixelsAndChangingPartsMakesAnotherTransparentRendering()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("A graphics device is needed for item pixels.");
            var catalog = Resources.Load<ItemCatalog>("Dustline/Items");
            Assert.That(catalog, Is.Not.Null, "Run Dustline > Prepare Project first.");
            var def = catalog.Find("rifle");
            var root = new GameObject("Model icon fixture");
            var kind = typeof(GameUI).Assembly.GetType("Dustline.ItemIconCache", true);
            var cache = (System.IDisposable)System.Activator.CreateInstance(kind, Members, null, new object[] { root.transform }, null);
            try
            {
                var first = Icon("Stock");
                var second = Icon("Same appearance, another instance");
                var selected = Icon("Configured");
                Bind(first, new ItemStack { ItemId = "rifle", InstanceId = "one" });
                Bind(second, new ItemStack { ItemId = "rifle", InstanceId = "two", Mods = new WeaponMods { OwnedSight = 2 } });
                var build = new ItemStack { ItemId = "rifle", InstanceId = "three" };
                build.Parts.Sight = new PartLink { ItemId = "part_sight_prism", InstanceId = "optic" };
                build.Parts.Grip = new PartLink { ItemId = "part_grip", InstanceId = "grip" };
                build.Parts.Stock = new PartLink { ItemId = "part_stock", InstanceId = "stock" };
                build.Parts.Muzzle = new PartLink { ItemId = "part_muzzle", InstanceId = "front" };
                build.Parts.Magazine = new PartLink { ItemId = "part_mag_a", InstanceId = "feed" };
                Bind(selected, build);
                for (int frame = 0; frame < 8 && selected.texture == null; frame++) { kind.GetMethod("Step", Members).Invoke(cache, null); yield return null; }
                Assert.That(first.texture, Is.Not.Null);
                Assert.That(second.texture, Is.SameAs(first.texture));
                Assert.That(selected.texture, Is.Not.Null.And.Not.SameAs(first.texture));
                var stockTexture = (RenderTexture)first.texture;
                Color32[] vanilla = Pixels(stockTexture);
                Color32[] fitted = Pixels((RenderTexture)selected.texture);
                Assert.That(SilhouetteWidth(vanilla, stockTexture.width), Is.InRange(.85f, .95f),
                    "The stock AR-20 must fit muzzle to stock within the long edge of its tile.");
                Assert.That(fitted[0].a, Is.LessThan(10), "The unused corners must be transparent.");
                int opaque = 0, changed = 0;
                for (int i = 0; i < fitted.Length; i++)
                {
                    if (fitted[i].a > 220) opaque++;
                    if (Mathf.Abs(vanilla[i].a - fitted[i].a) > 24 || Mathf.Abs(vanilla[i].r - fitted[i].r) > 24) changed++;
                }
                Assert.That(opaque, Is.GreaterThan(30), "This must render the model rather than a clear texture.");
                Assert.That(changed, Is.GreaterThan(30), "Installed geometry must affect the image.");
                ((RenderTexture)selected.texture).Release();
                Bind(selected, build.Clone());
                kind.GetMethod("Step", Members).Invoke(cache, null);
                Assert.That(((RenderTexture)selected.texture).IsCreated(), Is.True, "Recreate a lost Android render target.");
            }
            finally
            {
                cache.Dispose();
                Object.Destroy(root);
            }
            yield return null;

            RawImage Icon(string name)
            {
                var icon = new GameObject(name, typeof(RectTransform)).AddComponent<RawImage>();
                icon.transform.SetParent(root.transform, false);
                return icon;
            }
            void Bind(RawImage image, ItemStack stack) => kind.GetMethod("Bind", Members).Invoke(cache, new object[] { image, def, stack, catalog, WeaponForm.Tangdao });
        }

        static float SilhouetteWidth(Color32[] pixels, int width)
        {
            int left = width, right = -1;
            for (int i = 0; i < pixels.Length; i++)
            {
                if (pixels[i].a < 192) continue;
                int x = i % width;
                if (x < left) left = x;
                if (x > right) right = x;
            }
            return right < left ? 0f : (right - left + 1) / (float)width;
        }

        static Color32[] Pixels(RenderTexture texture)
        {
            var image = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
            var previous = RenderTexture.active;
            try
            {
                RenderTexture.active = texture;
                image.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
                return image.GetPixels32();
            }
            finally
            {
                RenderTexture.active = previous;
                Object.Destroy(image);
            }
        }
    }
}
