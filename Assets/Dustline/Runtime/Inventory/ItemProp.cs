using UnityEngine;
using UnityEngine.Rendering;

namespace Dustline
{
    internal sealed class ItemProp : MonoBehaviour
    {
        Ar20Mesh art;

        internal static Transform Create(Transform parent, ItemDefinition item, ItemStack stack, ItemCatalog catalog)
        {
            if (item.Weapon != null) return CreateWeapon(parent, item.Weapon.Form, stack, catalog).transform;
            return CreateObject(parent, item.Id, item).transform;
        }

        internal static Transform CreateLegacyPart(Transform parent, PartSlot slot, int level)
            => CreateObject(parent, "Factory part", null, slot, level).transform;

        static GameObject CreateObject(Transform parent, string name, ItemDefinition item, PartSlot slot = default, int level = 0)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer)) { layer = parent.gameObject.layer };
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<ItemProp>();
            view.art = Ar20Mesh.Acquire();
            go.GetComponent<MeshFilter>().sharedMesh = item != null ? view.art.ObjectMesh(item) : view.art.PartMesh(slot, level);
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = view.art.Material;
            renderer.receiveShadows = true;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            if (item != null && item.Equipment != EquipmentSlot.None && item.Tint.maxColorComponent > .01f)
            {
                var properties = new MaterialPropertyBlock();
                properties.SetColor("_BaseColor", Color.Lerp(Color.white, item.Tint, .45f));
                renderer.SetPropertyBlock(properties);
            }
            return go;
        }

        internal static WeaponVisual CreateWeapon(Transform parent, WeaponForm form, ItemStack stack, ItemCatalog catalog)
        {
            var view = WeaponVisual.Create(parent, false, form);
            view.SetConfiguration(stack, catalog);
            view.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            view.transform.localScale = Vector3.one;
            return view;
        }

        void OnDestroy() => art?.Release();
    }
}
