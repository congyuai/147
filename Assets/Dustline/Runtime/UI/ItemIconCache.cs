using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Dustline
{
    internal sealed class ItemIconCache : IDisposable
    {
        internal const string ComposeShader = "Hidden/Dustline/Item Icon Alpha";
        const int Limit = 116;
        readonly Transform owner;
        readonly Dictionary<(string id, int look), Entry> entries = new Dictionary<(string id, int look), Entry>();
        readonly Queue<Entry> pending = new Queue<Entry>();
        readonly Dictionary<RawImage, Entry> bindings = new Dictionary<RawImage, Entry>();
        readonly List<RawImage> stale = new List<RawImage>();
        Ar20Mesh sharedArt;
        ItemPreviewStage stage;
        Material compose;
        RenderTexture black, white;
        int access, frame;
        bool warned;

        sealed class Entry
        {
            internal (string id, int look) Key;
            internal ItemDefinition Item;
            internal ItemStack Stack;
            internal ItemCatalog Catalog;
            internal WeaponForm Blade;
            internal PartSlot LegacySlot;
            internal int LegacyLevel;
            internal readonly List<RawImage> Views = new List<RawImage>();
            internal RenderTexture Texture;
            internal int Access;
            internal bool Pending;
        }

        internal ItemIconCache(Transform owner) => this.owner = owner;

        internal static float Aspect(ItemDefinition item) => Mathf.Clamp(item == null ? 2f : item.Width / (float)item.Height, .25f, 5f);

        internal void Bind(RawImage image, ItemDefinition item, ItemStack stack, ItemCatalog catalog, WeaponForm blade = WeaponForm.Tangdao)
        {
            var key = (item?.Id ?? "@" + blade, WeaponRules.Look(stack, catalog));
            if (!entries.TryGetValue(key, out var entry))
            {
                entry = new Entry { Key = key, Item = item, Stack = stack?.Clone(), Catalog = catalog, Blade = blade };
                entries.Add(key, entry);
            }
            BindView(image, entry);
        }

        internal void BindLegacy(RawImage image, PartSlot slot, int level)
        {
            var key = ("@factory:" + slot, level);
            if (!entries.TryGetValue(key, out var entry))
            {
                entry = new Entry { Key = key, LegacySlot = slot, LegacyLevel = level };
                entries.Add(key, entry);
            }
            BindView(image, entry);
        }

        void BindView(RawImage image, Entry entry)
        {
            entry.Access = ++access;
            if (bindings.TryGetValue(image, out var old))
            {
                if (old != entry) old.Views.Remove(image);
            }
            if (image is CachedItemImage cached) cached.VisibilityChanged = () => OnVisibilityChanged(image);
            bindings[image] = entry;
            if (!entry.Views.Contains(image)) entry.Views.Add(image);
            bool ready = entry.Texture != null && entry.Texture.IsCreated();
            image.texture = ready ? entry.Texture : null;
            image.color = ready ? Color.white : Color.clear;
            if (!ready) Queue(entry);
        }

        void OnVisibilityChanged(RawImage image)
        {
            if (Visible(image) && bindings.TryGetValue(image, out var entry) && (entry.Texture == null || !entry.Texture.IsCreated())) Queue(entry);
        }

        internal void Unbind(RawImage image)
        {
            if (bindings.TryGetValue(image, out var entry))
            {
                entry.Views.Remove(image);
                bindings.Remove(image);
                if (image is CachedItemImage cached) cached.VisibilityChanged = null;
            }
            image.texture = null;
            image.color = Color.clear;
        }

        void Queue(Entry entry)
        {
            if (entry.Pending || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;
            entry.Pending = true;
            pending.Enqueue(entry);
        }

        internal void Step()
        {
            if ((++frame & 31) == 0)
            {
                stale.Clear();
                foreach (var pair in bindings)
                {
                    if (pair.Key == null) { stale.Add(pair.Key); continue; }
                    var entry = pair.Value;
                    if (Visible(pair.Key) && (entry.Texture == null || !entry.Texture.IsCreated())) Queue(entry);
                }
                foreach (var view in stale)
                {
                    bindings[view].Views.Remove(view);
                    bindings.Remove(view);
                }
            }
            if (GraphicsSettings.currentRenderPipeline != null)
                for (int i = 0; i < 24 && pending.Count > 0; i++)
                {
                    var entry = pending.Dequeue();
                    entry.Pending = false;
                    if (!entry.Views.Exists(Visible)) continue;
                    Capture(entry);
                    break;
                }
            if (entries.Count <= Limit) return;
            Entry oldest = null;
            foreach (var entry in entries.Values)
            {
                entry.Views.RemoveAll(view => view == null);
                if (!entry.Pending && entry.Views.Count == 0 && (oldest == null || entry.Access < oldest.Access)) oldest = entry;
            }
            if (oldest == null) return;
            entries.Remove(oldest.Key);
            Release(oldest.Texture);
        }

        static bool Visible(RawImage view) => view != null && view.gameObject.activeInHierarchy && (view.canvas == null || !view.canvasRenderer.cull);

        void Capture(Entry entry)
        {
            var shader = Shader.Find(ComposeShader);
            if (shader == null || !shader.isSupported) { ReportUnavailable(); return; }
            compose ??= new Material(shader) { hideFlags = HideFlags.DontSave };
            sharedArt ??= Ar20Mesh.Acquire();
            stage ??= new ItemPreviewStage(owner, 0);
            float aspect = entry.LegacyLevel > 0 ? 1f : Aspect(entry.Item);
            int w = Mathf.Max(40, Mathf.RoundToInt(200f * Mathf.Min(1f, aspect)));
            int h = Mathf.Max(40, Mathf.RoundToInt(200f * Mathf.Min(1f, 1f / aspect)));
            if (black == null || black.width != w || black.height != h)
            {
                Release(black); Release(white);
                black = Texture(w, h, 24); white = Texture(w, h, 24);
            }
            try
            {
                if (entry.LegacyLevel > 0) stage.SetLegacyPart(entry.LegacySlot, entry.LegacyLevel);
                else stage.Set(entry.Item, entry.Stack, entry.Catalog, entry.Blade);
                if (!stage.Render(black, Color.black) || !stage.Render(white, Color.white)) { ReportUnavailable(); return; }
                if (entry.Texture == null) entry.Texture = Texture(w, h, 0);
                if (!entry.Texture.IsCreated() && !entry.Texture.Create()) return;
                compose.SetTexture("_WhiteTex", white);
                var previous = RenderTexture.active;
                try { Graphics.Blit(black, entry.Texture, compose); }
                finally { RenderTexture.active = previous; }
                foreach (var image in entry.Views)
                    if (image != null) { image.texture = entry.Texture; image.color = Color.white; }
            }
            finally { stage.Clear(); }
        }

        void ReportUnavailable()
        {
            if (warned) return;
            warned = true;
            Debug.LogWarning("物品模型预览不可用，请检查渲染管线和图标着色器。");
        }

        internal static RenderTexture Texture(int w, int h, int depth) => new RenderTexture(w, h, depth, RenderTextureFormat.ARGB32)
        {
            name = "Dustline item render", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave
        };

        internal static void Release(RenderTexture texture)
        {
            if (texture == null) return;
            texture.Release();
            Object.Destroy(texture);
        }

        public void Dispose()
        {
            foreach (var view in bindings.Keys) if (view is CachedItemImage cached && cached != null) cached.VisibilityChanged = null;
            foreach (var entry in entries.Values) Release(entry.Texture);
            entries.Clear();
            bindings.Clear();
            pending.Clear();
            stage?.Dispose();
            Release(black); Release(white);
            if (compose != null) Object.Destroy(compose);
            sharedArt?.Release();
        }
    }

    internal sealed class CachedItemImage : RawImage
    {
        internal Action VisibilityChanged;

        protected override void OnEnable()
        {
            base.OnEnable();
            VisibilityChanged?.Invoke();
        }

        public override void Cull(Rect clipRect, bool validRect)
        {
            bool wasCulled = canvasRenderer.cull;
            base.Cull(clipRect, validRect);
            if (wasCulled && !canvasRenderer.cull) VisibilityChanged?.Invoke();
        }
    }
}
