namespace UnityEngine.Rendering.Universal.Internal
{
    /// <summary>
    /// Copy the given color target to the current camera target
    ///
    /// You can use this pass to copy the result of rendering to
    /// the camera target. The pass takes the screen viewport into
    /// consideration.
    /// </summary>
    public class FinalBlitPass : ScriptableRenderPass
    {
        const string m_ProfilerTag = "Final Blit Pass";
        RenderTargetHandle m_Source;
        Material m_BlitMaterial;
        TextureDimension m_TargetDimension;
        bool m_IsMobileOrSwitch;

        public FinalBlitPass(RenderPassEvent evt, Material blitMaterial)
        {
            m_BlitMaterial = blitMaterial;
            renderPassEvent = evt;
        }

        /// <summary>
        /// Configure the pass
        /// </summary>
        /// <param name="baseDescriptor"></param>
        /// <param name="colorHandle"></param>
        public void Setup(RenderTextureDescriptor baseDescriptor, RenderTargetHandle colorHandle)
        {
            m_Source = colorHandle;
            m_TargetDimension = baseDescriptor.dimension;
            m_IsMobileOrSwitch = Application.isMobilePlatform || Application.platform == RuntimePlatform.Switch;
        }

        /// <inheritdoc/>
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (m_BlitMaterial == null)
            {
                Debug.LogErrorFormat("Missing {0}. {1} render pass will not execute. Check for missing reference in the renderer resources.", m_BlitMaterial, GetType().Name);
                return;
            }

            bool requiresSRGBConvertion = Display.main.requiresSrgbBlitToBackbuffer;

            CommandBuffer cmd = CommandBufferPool.Get(m_ProfilerTag);

            if (requiresSRGBConvertion)
                cmd.EnableShaderKeyword(ShaderKeywordStrings.LinearToSRGBConversion);
            else
                cmd.DisableShaderKeyword(ShaderKeywordStrings.LinearToSRGBConversion);

            // Note: We need to get the cameraData.targetTexture as this will get the targetTexture of the camera stack.
            // Overlay cameras need to output to the target described in the base camera while doing camera stack.
            // XRTDOO: verify camera stack still works. cameraData.targetTexture is not configured in xrPass.renderTarget
            ref CameraData cameraData = ref renderingData.cameraData;
            RenderTargetIdentifier blitTarget;
            if (!cameraData.xrPass.hasMultiXrView)
            {
                blitTarget = new RenderTargetIdentifier(cameraData.xrPass.renderTarget, 0, CubemapFace.Unknown, cameraData.xrPass.GetTextureArraySlice(0));
            }
            else
            {
                blitTarget = new RenderTargetIdentifier(cameraData.xrPass.renderTarget, 0, CubemapFace.Unknown, -1);
            }

            // TODO: Final blit pass should always blit to backbuffer. The first time we do we don't need to Load contents to tile.
            // We need to keep in the pipeline of first render pass to each render target to propertly set load/store actions.
            // meanwhile we set to load so split screen case works.
            CoreUtils.SetRenderTarget(cmd, blitTarget, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, ClearFlag.None, Color.black);
            cmd.SetViewport(cameraData.xrPass.GetViewport(0));

            // We f-flip if
            // 1) we are bliting from render texture to back buffer(UV starts at bottom) and
            // 2) renderTexture starts UV at top
            bool yflip =  !cameraData.xrPass.renderTargetIsRenderTexture && SystemInfo.graphicsUVStartsAtTop;
            Vector4 scaleBias = yflip ? new Vector4(1, -1, 0, 1) : new Vector4(1, 1, 0, 0); ;
            Vector4 scaleBiasRT = new Vector4(1, 1, 0, 0);
            cmd.SetGlobalVector(ShaderConstants._BlitScaleBias, scaleBias);
            cmd.SetGlobalVector(ShaderConstants._BlitScaleBiasRt, scaleBiasRT);
            cmd.SetGlobalTexture("_BlitTex", m_Source.Identifier());

            cmd.DrawProcedural(Matrix4x4.identity, m_BlitMaterial, 0, MeshTopology.Quads, 4, 1, null);

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        // Precomputed shader ids to save some CPU cycles (mostly affects mobile)
        static class ShaderConstants
        {
            public static readonly int _BlitScaleBias = Shader.PropertyToID("_BlitScaleBias");
            public static readonly int _BlitScaleBiasRt = Shader.PropertyToID("_BlitScaleBiasRt");
        }
    }
}
