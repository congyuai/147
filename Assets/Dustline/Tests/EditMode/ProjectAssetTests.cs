using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Dustline.Tests
{
    public sealed class ProjectAssetTests
    {
        [Test]
        public void CatalogAndLootHaveResolvableReferences()
        {
            var catalog = Resources.Load<ItemCatalog>("Dustline/Items");
            var loot = Resources.Load<LootTable>("Dustline/Loot");
            Assert.That(catalog, Is.Not.Null, "Run Dustline > Prepare Project first.");
            Assert.That(loot, Is.Not.Null);
            Assert.DoesNotThrow(catalog.RebuildIndex);
            Assert.That(catalog.Items.Count, Is.EqualTo(6));
            foreach (var item in catalog.Items)
                if (item.Kind == ItemKind.Weapon)
                {
                    Assert.That(item.Weapon, Is.Not.Null);
                    Assert.That(catalog.Find(item.Weapon.AmmoId).Kind, Is.EqualTo(ItemKind.Ammo));
                }
            foreach (var entry in loot.Entries) Assert.That(catalog.Find(entry.ItemId), Is.Not.Null);
        }

        [Test]
        public void BootstrapSceneAndPipelineAreConfigured()
        {
            Assert.That(GraphicsSettings.defaultRenderPipeline, Is.Not.Null);
            Assert.That(EditorBuildSettings.scenes.Length, Is.EqualTo(1));
            Assert.That(EditorBuildSettings.scenes[0].enabled, Is.True);
            Assert.That(AssetDatabase.LoadAssetAtPath<SceneAsset>(EditorBuildSettings.scenes[0].path), Is.Not.Null);
            Assert.That(Shader.Find("Universal Render Pipeline/Lit"), Is.Not.Null);
            Assert.That(Shader.Find("Universal Render Pipeline/Unlit"), Is.Not.Null);
        }
    }
}
