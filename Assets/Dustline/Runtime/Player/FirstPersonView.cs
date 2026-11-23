using System;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Dustline
{
    public static class FirstPersonView
    {
        public const string LayerName = "DustlineViewModel";
        public const int Layer = 28;
        public const int Mask = 1 << Layer;

        internal static Camera Create(Camera world)
        {
            var baseData = world.GetUniversalAdditionalCameraData();
            baseData.renderType = CameraRenderType.Base;
            baseData.SetRenderer(0);
            var stack = baseData.cameraStack;
            if (stack == null) throw new InvalidOperationException("The raid camera requires the URP forward camera stack.");

            var node = new GameObject("First person foreground", typeof(Camera)) { layer = Layer };
            node.transform.SetParent(world.transform, false);
            var overlay = node.GetComponent<Camera>();
            overlay.cullingMask = Mask;
            overlay.clearFlags = CameraClearFlags.Depth;
            overlay.nearClipPlane = 0.025f;
            overlay.farClipPlane = 6f;
            overlay.fieldOfView = world.fieldOfView;
            overlay.rect = world.rect;
            overlay.allowMSAA = world.allowMSAA;
            overlay.allowHDR = world.allowHDR;
            overlay.useOcclusionCulling = false;
            var data = overlay.GetUniversalAdditionalCameraData();
            data.renderType = CameraRenderType.Overlay;
            data.SetRenderer(0);
            data.renderShadows = false;
            data.renderPostProcessing = false;
            data.requiresColorOption = CameraOverrideOption.Off;
            data.requiresDepthOption = CameraOverrideOption.Off;
            // New URP overlay data clears depth by default; preserve local mesh depth testing.
            if (!data.clearDepth) throw new InvalidOperationException("The foreground camera must clear world depth.");
            world.cullingMask &= ~(Mask | ItemPreviewStage.LayerMask);
            stack.Add(overlay);
            return overlay;
        }
    }
}
