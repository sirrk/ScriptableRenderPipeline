using System.Collections.Generic;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering;
using UnityEngine.Scripting.APIUpdating;

namespace UnityEngine.Experimental.Rendering.Universal
{
    [MovedFrom("UnityEngine.Experimental.Rendering.LWRP")] public class RenderObjectsPass : ScriptableRenderPass
    {
        RenderQueueType renderQueueType;
        FilteringSettings m_FilteringSettings;
        RenderObjects.CustomCameraSettings m_CameraSettings;
        string m_ProfilerTag;
        ProfilingSampler m_ProfilingSampler;

        public Material overrideMaterial { get; set; }
        public int overrideMaterialPassIndex { get; set; }

        List<ShaderTagId> m_ShaderTagIdList = new List<ShaderTagId>();

        public void SetDetphState(bool writeEnabled, CompareFunction function = CompareFunction.Less)
        {
            m_RenderStateBlock.mask |= RenderStateMask.Depth;
            m_RenderStateBlock.depthState = new DepthState(writeEnabled, function);
        }

        public void SetStencilState(int reference, CompareFunction compareFunction, StencilOp passOp, StencilOp failOp, StencilOp zFailOp)
        {
            StencilState stencilState = StencilState.defaultValue;
            stencilState.enabled = true;
            stencilState.SetCompareFunction(compareFunction);
            stencilState.SetPassOperation(passOp);
            stencilState.SetFailOperation(failOp);
            stencilState.SetZFailOperation(zFailOp);

            m_RenderStateBlock.mask |= RenderStateMask.Stencil;
            m_RenderStateBlock.stencilReference = reference;
            m_RenderStateBlock.stencilState = stencilState;
        }

        RenderStateBlock m_RenderStateBlock;

        public RenderObjectsPass(string profilerTag, RenderPassEvent renderPassEvent, string[] shaderTags, RenderQueueType renderQueueType, int layerMask, RenderObjects.CustomCameraSettings cameraSettings)
        {
            m_ProfilerTag = profilerTag;
            m_ProfilingSampler = new ProfilingSampler(profilerTag);
            this.renderPassEvent = renderPassEvent;
            this.renderQueueType = renderQueueType;
            this.overrideMaterial = null;
            this.overrideMaterialPassIndex = 0;
            RenderQueueRange renderQueueRange = (renderQueueType == RenderQueueType.Transparent)
                ? RenderQueueRange.transparent
                : RenderQueueRange.opaque;
            m_FilteringSettings = new FilteringSettings(renderQueueRange, layerMask);

            if (shaderTags != null && shaderTags.Length > 0)
            {
                foreach (var passName in shaderTags)
                    m_ShaderTagIdList.Add(new ShaderTagId(passName));
            }
            else
            {
                m_ShaderTagIdList.Add(new ShaderTagId("UniversalForward"));
                m_ShaderTagIdList.Add(new ShaderTagId("LightweightForward"));
                m_ShaderTagIdList.Add(new ShaderTagId("SRPDefaultUnlit"));
            }

            m_RenderStateBlock = new RenderStateBlock(RenderStateMask.Nothing);
            m_CameraSettings = cameraSettings;

        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            SortingCriteria sortingCriteria = (renderQueueType == RenderQueueType.Transparent)
                ? SortingCriteria.CommonTransparent
                : renderingData.cameraData.defaultOpaqueSortFlags;

            DrawingSettings drawingSettings = CreateDrawingSettings(m_ShaderTagIdList, ref renderingData, sortingCriteria);
            drawingSettings.overrideMaterial = overrideMaterial;
            drawingSettings.overrideMaterialPassIndex = overrideMaterialPassIndex;

            ref CameraData cameraData = ref renderingData.cameraData;

            CommandBuffer cmd = CommandBufferPool.Get(m_ProfilerTag);
            using (new ProfilingScope(cmd, m_ProfilingSampler))
            {
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                bool isRenderToRenderTexture = colorAttachment != cameraData.xrPass.renderTarget || cameraData.xrPass.renderTargetIsRenderTexture;
                // if contains only 1 view, setup view proj
                if (!cameraData.xrPass.hasMultiXrView)
                {
                    // XR Pass viewport will handle camera stack too
                    Rect pixelRect = cameraData.xrPass.GetViewport(0);
                    float cameraAspect = (float)pixelRect.width / (float)pixelRect.height;

                    Matrix4x4 viewMatrix = cameraData.xrPass.GetViewMatrix(0);
                    Matrix4x4 projectionMatrix = cameraData.xrPass.GetProjMatrix(0);
                    if (m_CameraSettings.overrideCamera)
                    {
                        Camera camera = cameraData.camera;
                        projectionMatrix = Matrix4x4.Perspective(m_CameraSettings.cameraFieldOfView, cameraAspect,
                            camera.nearClipPlane, camera.farClipPlane);

                        Vector4 cameraTranslation = viewMatrix.GetColumn(3);
                        viewMatrix.SetColumn(3, cameraTranslation + m_CameraSettings.offset);
                    }
                    projectionMatrix = GL.GetGPUProjectionMatrix(projectionMatrix, isRenderToRenderTexture);

                    RenderingUtils.SetViewProjectionMatrices(cmd, viewMatrix, projectionMatrix, false);
                }
                // else, set up multi view proj to stereo buffer
                else
                {
                    //XRTODO: compute stereo data while constructing XRPass
                    Matrix4x4[] stereoProjectionMatrix = new Matrix4x4[2];
                    Matrix4x4[] stereoViewMatrix = new Matrix4x4[2];

                    for (int i = 0; i < 2; i++)
                    {
                        // XR Pass viewport will handle camera stack too
                        Rect pixelRect = cameraData.xrPass.GetViewport(i);
                        float cameraAspect = (float)pixelRect.width / (float)pixelRect.height;

                        Matrix4x4 viewMatrix = cameraData.xrPass.GetViewMatrix(i);
                        Matrix4x4 projectionMatrix = cameraData.xrPass.GetProjMatrix(i);
                        if (m_CameraSettings.overrideCamera)
                        {
                            Camera camera = cameraData.camera;
                            projectionMatrix = Matrix4x4.Perspective(m_CameraSettings.cameraFieldOfView, cameraAspect,
                                camera.nearClipPlane, camera.farClipPlane);

                            Vector4 cameraTranslation = viewMatrix.GetColumn(3);
                            viewMatrix.SetColumn(3, cameraTranslation + m_CameraSettings.offset);
                        }

                        stereoViewMatrix[i] = viewMatrix;
                        stereoProjectionMatrix[i] = GL.GetGPUProjectionMatrix(projectionMatrix, isRenderToRenderTexture);
                    }
                    RenderingUtils.SetStereoViewProjectionMatrices(cmd, stereoViewMatrix, stereoProjectionMatrix, false);
                }
                    
                context.ExecuteCommandBuffer(cmd);

                context.DrawRenderers(renderingData.cullResults, ref drawingSettings, ref m_FilteringSettings,
                    ref m_RenderStateBlock);

                if (m_CameraSettings.overrideCamera && m_CameraSettings.restoreCamera)
                {
                    cmd.Clear();
                    
                    // if contains only 1 view, setup view proj
                    if (!cameraData.xrPass.hasMultiXrView)
                    {
                        Matrix4x4 viewMatrix = cameraData.xrPass.GetViewMatrix(0);
                        Matrix4x4 projectionMatrix = cameraData.xrPass.GetProjMatrix(0);
                        RenderingUtils.SetViewProjectionMatrices(cmd, viewMatrix, GL.GetGPUProjectionMatrix(projectionMatrix, isRenderToRenderTexture), false);
                    }
                    else
                    {
                        //XRTODO: compute stereo data while constructing XRPass
                        Matrix4x4[] stereoProjectionMatrix = new Matrix4x4[2];
                        Matrix4x4[] stereoViewMatrix = new Matrix4x4[2];
                        for (int i = 0; i < 2; i++)
                        {
                            Matrix4x4 viewMatrix = cameraData.xrPass.GetViewMatrix(i);
                            Matrix4x4 projectionMatrix = cameraData.xrPass.GetProjMatrix(i);
                            stereoViewMatrix[i] = viewMatrix;
                            stereoProjectionMatrix[i] = GL.GetGPUProjectionMatrix(projectionMatrix, isRenderToRenderTexture);
                        }
                        RenderingUtils.SetStereoViewProjectionMatrices(cmd, stereoViewMatrix, stereoProjectionMatrix, false);
                    }
                    
                }
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }
}
