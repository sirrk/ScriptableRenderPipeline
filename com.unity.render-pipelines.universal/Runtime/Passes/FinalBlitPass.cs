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
            ref CameraData cameraData = ref renderingData.cameraData;

            if (!cameraData.xrPass.hasMultiXrView)
            {
                // TODO: Final blit pass should always blit to backbuffer. The first time we do we don't need to Load contents to tile.
                // We need to keep in the pipeline of first render pass to each render target to propertly set load/store actions.
                // meanwhile we set to load so split screen case works.
                if (m_TargetDimension == TextureDimension.Tex2DArray)
                    CoreUtils.SetRenderTarget(cmd, cameraData.xrPass.renderTarget, ClearFlag.None, Color.black, 0, CubemapFace.Unknown, cameraData.xrPass.GetTextureArraySlice(0));
                else
                    CoreUtils.SetRenderTarget(cmd, cameraData.xrPass.renderTarget, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, ClearFlag.None, Color.black);
                cmd.SetViewport(cameraData.xrPass.GetViewport(0));

                Matrix4x4 projMatrix = GL.GetGPUProjectionMatrix(Matrix4x4.identity, cameraData.xrPass.renderTargetIsRenderTexture);
                RenderingUtils.SetViewProjectionMatrices(cmd, Matrix4x4.identity, projMatrix, true);
            }
            else
            {
                CoreUtils.SetRenderTarget(cmd, cameraData.xrPass.renderTarget, ClearFlag.None, Color.black, 0, CubemapFace.Unknown, -1);
                cmd.SetViewport(cameraData.xrPass.GetViewport(0));

                // XRTODO: this is blit shader, we use projection matrix to handle y flip. no need to passing stereo projection matrix here. just pass 1 proj is good enough
                // XRTODO: replace this with full screen quad. And handle y flip without drawing quad geometry.
                Matrix4x4[] stereoProjectionMatrix = new Matrix4x4[2];
                Matrix4x4[] stereoViewMatrix = new Matrix4x4[2];
                for (int i = 0; i < 2; i++)
                {
                    stereoViewMatrix[i] = Matrix4x4.identity;
                    stereoProjectionMatrix[i] = GL.GetGPUProjectionMatrix(Matrix4x4.identity, cameraData.xrPass.renderTargetIsRenderTexture);
                }

                RenderingUtils.SetStereoViewProjectionMatrices(cmd, stereoViewMatrix, stereoProjectionMatrix, true);
            }

            cmd.SetGlobalTexture("_BlitTex", m_Source.Identifier());


            cmd.DrawMesh(RenderingUtils.fullscreenMesh, Matrix4x4.identity, m_BlitMaterial);
            RenderingUtils.SetViewProjectionMatrices(cmd, cameraData.camera.worldToCameraMatrix,
                GL.GetGPUProjectionMatrix(cameraData.camera.projectionMatrix, cameraData.xrPass.renderTargetIsRenderTexture), true);

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }
}
