using System;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Dustline
{
    sealed class OperatorPreview : IDisposable
    {
        const int Layer = 30;
        readonly GameObject stage;
        readonly Camera camera;
        readonly RenderTexture texture;
        readonly WeaponDefinition loadout;
        OperatorVisual model;
        WeaponVisual weapon;
        OperatorStyle selected;

        public OperatorPreview(Transform parent, RawImage image, WeaponDefinition loadout)
        {
            this.loadout = loadout;
            stage = new GameObject("Operator preview stage") { layer = Layer };
            stage.transform.SetParent(parent, false);
            stage.transform.position = new Vector3(0, -300, 0);
            texture = new RenderTexture(336, 400, 24, RenderTextureFormat.ARGB32) { name = "Operator portrait" };
            image.texture = texture;
            var eye = new GameObject("Portrait camera", typeof(Camera));
            eye.transform.SetParent(stage.transform, false);
            eye.transform.localPosition = new Vector3(1.3f, 1.32f, 3.2f);
            eye.transform.LookAt(stage.transform.position + Vector3.up * 0.87f);
            camera = eye.GetComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 1.03f;
            camera.nearClipPlane = 0.10f;
            camera.farClipPlane = 9;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color32(27, 43, 47, 255);
            camera.cullingMask = 1 << Layer;
            camera.targetTexture = texture;
            camera.allowMSAA = false;
            camera.allowHDR = false;
            Lamp("Key", new Vector3(0.1f, 2.8f, 2.9f), new Color(1, 0.82f, 0.64f), 10);
            Lamp("Fill", new Vector3(-1.8f, 1.15f, 1.8f), new Color(0.60f, 0.83f, 1), 7);
        }

        void Lamp(string name, Vector3 position, Color color, float intensity)
        {
            var lamp = new GameObject(name, typeof(Light));
            lamp.transform.SetParent(stage.transform, false);
            lamp.transform.localPosition = position;
            var light = lamp.GetComponent<Light>();
            light.type = LightType.Point;
            light.intensity = intensity;
            light.range = 9;
            light.color = color;
            light.cullingMask = 1 << Layer;
            light.shadows = LightShadows.None;
        }

        public void Select(OperatorStyle style)
        {
            if (model != null && selected == style) return;
            if (model != null)
            {
                model.gameObject.SetActive(false);
                Object.Destroy(model.gameObject);
            }
            selected = style;
            model = OperatorVisual.Create(stage.transform, style, 0, Layer);
            model.transform.localRotation = Quaternion.Euler(0, -16, 0);
            model.SetMotion(0, false, false, 0.016f);
            if (loadout == null) return;
            weapon = WeaponVisual.Create(model.WeaponMount, false, loadout.Melee);
            model.PoseArms(weapon.RightGrip, weapon.LeftGrip);
        }

        public void Dispose()
        {
            camera.targetTexture = null;
            stage.SetActive(false);
            texture.Release();
            Object.Destroy(texture);
            Object.Destroy(stage);
        }
    }
}
