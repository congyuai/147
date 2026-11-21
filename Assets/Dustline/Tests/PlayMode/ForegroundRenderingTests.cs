using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Dustline.Tests
{
    public sealed class ForegroundRenderingTests
    {
        [Test]
        public void CameraStackClearsWorldDepthButKeepsForegroundDepthAndWorldColor()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("A graphics device is required for pixel readback.");
            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            Assert.That(pipeline, Is.Not.Null, "Run Dustline > Prepare Project first.");
            var root = new GameObject("Foreground render test");
            var target = new RenderTexture(128, 128, 24, RenderTextureFormat.ARGB32);
            var readback = new Texture2D(128, 128, TextureFormat.RGB24, false);
            var oldActive = RenderTexture.active;
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            var wall = new Material(shader) { color = Color.red };
            var near = new Material(shader) { color = Color.green };
            var far = new Material(shader) { color = Color.blue };
            try
            {
                root.transform.position = new Vector3(2400f, 30f, -2100f);
                var camera = root.AddComponent<Camera>();
                camera.enabled = false;
                camera.nearClipPlane = 0.09f;
                camera.farClipPlane = 80f;
                camera.fieldOfView = 60f;
                camera.aspect = 1f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                var data = camera.GetUniversalAdditionalCameraData();
                data.renderPostProcessing = false;
                var create = typeof(FirstPersonView).GetMethod("Create", BindingFlags.Static | BindingFlags.NonPublic);
                var overlay = (Camera)create.Invoke(null, new object[] { camera });
                overlay.aspect = 1f;
                Assert.That(data.cameraStack, Is.EquivalentTo(new[] { overlay }));
                Assert.That(camera.cullingMask & FirstPersonView.Mask, Is.Zero);
                Assert.That(camera.cullingMask & (1 << 2), Is.Not.EqualTo(0));
                Assert.That(overlay.cullingMask, Is.EqualTo(FirstPersonView.Mask));
                Assert.That(overlay.GetUniversalAdditionalCameraData().clearDepth, Is.True);
                Assert.That(overlay.GetUniversalAdditionalCameraData().renderType, Is.EqualTo(CameraRenderType.Overlay));
                Assert.That(overlay.GetComponent<AudioListener>(), Is.Null);
                Cube(0.42f, 1.2f, 0, wall);
                Cube(1.2f, 1.0f, FirstPersonView.Layer, far);
                Cube(0.96f, 0.30f, FirstPersonView.Layer, near);

                target.Create();
                var request = new RenderPipeline.StandardRequest { destination = target };
                Assert.That(RenderPipeline.SupportsRenderRequest(camera, request), Is.True);
                RenderPipeline.SubmitRenderRequest(camera, request);
                RenderTexture.active = target;
                readback.ReadPixels(new Rect(0f, 0f, 128f, 128f), 0, 0);
                var center = readback.GetPixel(64, 64);
                var side = readback.GetPixel(95, 64);
                var edge = readback.GetPixel(122, 64);
                Assert.That(center.g, Is.GreaterThan(center.r + 0.5f).And.GreaterThan(center.b + 0.5f), "The near foreground must win its own depth test.");
                Assert.That(side.b, Is.GreaterThan(side.r + 0.5f).And.GreaterThan(side.g + 0.5f), "The farther foreground must still appear over the nearer wall.");
                Assert.That(edge.r, Is.GreaterThan(edge.g + 0.5f).And.GreaterThan(edge.b + 0.5f), "The overlay must not clear the world color.");

                void Cube(float depth, float size, int layer, Material material)
                {
                    var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    cube.transform.SetParent(root.transform, false);
                    cube.transform.localPosition = Vector3.forward * depth;
                    cube.transform.localScale = new Vector3(size, size, 0.06f);
                    cube.layer = layer;
                    cube.GetComponent<MeshRenderer>().sharedMaterial = material;
                }
            }
            finally
            {
                RenderTexture.active = oldActive;
                target.Release();
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(readback);
                Object.DestroyImmediate(wall);
                Object.DestroyImmediate(near);
                Object.DestroyImmediate(far);
            }
        }
    }
}
