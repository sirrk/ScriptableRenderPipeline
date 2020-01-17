using System;

namespace UnityEngine.Rendering.Universal.Internal
{
    /// <summary>
    /// Copy the given depth buffer into the given destination depth buffer.
    /// 
    /// You can use this pass to copy a depth buffer to a destination,
    /// so you can use it later in rendering. If the source texture has MSAA
    /// enabled, the pass uses a custom MSAA resolve. If the source texture
    /// does not have MSAA enabled, the pass uses a Blit or a Copy Texture
    /// operation, depending on what the current platform supports.
    /// </summary>
    public class CopyDepthPass : ScriptableRenderPass
    {
        private RenderTargetHandle source { get; set; }
        private RenderTargetHandle destination { get; set; }
        Material m_CopyDepthMaterial;
        RenderTextureDescriptor m_Descriptor;
        const string m_ProfilerTag = "Copy Depth";
        public CopyDepthPass(RenderPassEvent evt, Material copyDepthMaterial)
        {
            m_CopyDepthMaterial = copyDepthMaterial;
            renderPassEvent = evt;
        }

        /// <summary>
        /// Configure the pass with the source and destination to execute on.
        /// </summary>
        /// <param name="source">Source Render Target</param>
        /// <param name="destination">Destination Render Targt</param>
        public void Setup(RenderTargetHandle source, RenderTargetHandle destination)
        {
            this.source = source;
            this.destination = destination;
        }

        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            m_Descriptor = cameraTextureDescriptor;
            m_Descriptor.colorFormat = RenderTextureFormat.Depth;
            m_Descriptor.depthBufferBits = 32; //TODO: do we really need this. double check;
            m_Descriptor.msaaSamples = 1;
            cmd.GetTemporaryRT(destination.id, m_Descriptor, FilterMode.Point);
        }

        /// <inheritdoc/>
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (m_CopyDepthMaterial == null)
            {
                Debug.LogErrorFormat("Missing {0}. {1} render pass will not execute. Check for missing reference in the renderer resources.", m_CopyDepthMaterial, GetType().Name);
                return;
            }

            CommandBuffer cmd = CommandBufferPool.Get(m_ProfilerTag);
            RenderTargetIdentifier depthSurface = source.Identifier();
            RenderTargetIdentifier copyDepthSurface = destination.Identifier();

            Vector4 scaleBias = new Vector4(1, 1, 0, 0);
            Vector4 scaleBiasRT = new Vector4(1, 1, 0, 0);
            cmd.SetGlobalVector(ShaderConstants._BlitScaleBias, scaleBias);
            cmd.SetGlobalVector(ShaderConstants._BlitScaleBiasRt, scaleBiasRT);
            cmd.SetGlobalTexture("_CameraDepthAttachment", source.Identifier());

            RenderTextureDescriptor descriptor = m_Descriptor;
            int cameraSamples = descriptor.msaaSamples;
            switch (cameraSamples)
            {
                case 1:
                    cmd.EnableShaderKeyword(ShaderKeywordStrings.DepthNoMsaa);
                    cmd.DisableShaderKeyword(ShaderKeywordStrings.DepthMsaa2);
                    cmd.DisableShaderKeyword(ShaderKeywordStrings.DepthMsaa4);
                    break;
                case 2:
                    cmd.DisableShaderKeyword(ShaderKeywordStrings.DepthNoMsaa);
                    cmd.EnableShaderKeyword(ShaderKeywordStrings.DepthMsaa2);
                    cmd.DisableShaderKeyword(ShaderKeywordStrings.DepthMsaa4);
                    break;
                case 4:
                    cmd.DisableShaderKeyword(ShaderKeywordStrings.DepthNoMsaa);
                    cmd.DisableShaderKeyword(ShaderKeywordStrings.DepthMsaa2);
                    cmd.EnableShaderKeyword(ShaderKeywordStrings.DepthMsaa4);
                    break;
                default:
                    // XRTODO: Add err msg. This case shouldn't really happend. Could be undefined behavior
                    cmd.EnableShaderKeyword(ShaderKeywordStrings.DepthNoMsaa);
                    cmd.DisableShaderKeyword(ShaderKeywordStrings.DepthMsaa2);
                    cmd.DisableShaderKeyword(ShaderKeywordStrings.DepthMsaa4);
                    break;
            }

            ScriptableRenderer.SetRenderTarget(cmd, new RenderTargetIdentifier(copyDepthSurface, 0, CubemapFace.Unknown, -1),
                                               BuiltinRenderTextureType.CameraTarget, clearFlag, clearColor);
            
            cmd.DrawProcedural(Matrix4x4.identity, m_CopyDepthMaterial, 0, MeshTopology.Quads, 4, 1, null);
            
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        void CopyTexture(CommandBuffer cmd, RenderTargetIdentifier source, RenderTargetIdentifier dest, Material material)
        {
            // TODO: In order to issue a copyTexture we need to also check if source and dest have same size
            //if (SystemInfo.copyTextureSupport != CopyTextureSupport.None)
            //    cmd.CopyTexture(source, dest);
            //else
            Blit(cmd, source, dest, material);
        }

        /// <inheritdoc/>
        public override void FrameCleanup(CommandBuffer cmd)
        {
            if (cmd == null)
                throw new ArgumentNullException("cmd");

            cmd.ReleaseTemporaryRT(destination.id);
            destination = RenderTargetHandle.CameraTarget;
        }

        // Precomputed shader ids to save some CPU cycles (mostly affects mobile)
        static class ShaderConstants
        {
            public static readonly int _BlitScaleBias = Shader.PropertyToID("_BlitScaleBias");
            public static readonly int _BlitScaleBiasRt = Shader.PropertyToID("_BlitScaleBiasRt");
        }
    }
}
