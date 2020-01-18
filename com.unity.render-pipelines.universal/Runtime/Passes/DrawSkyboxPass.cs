namespace UnityEngine.Rendering.Universal.Internal
{
    /// <summary>
    /// Draw the skybox into the given color buffer using the given depth buffer for depth testing.
    ///
    /// This pass renders the standard Unity skybox.
    /// </summary>
    public class DrawSkyboxPass : ScriptableRenderPass
    {
        public DrawSkyboxPass(RenderPassEvent evt)
        {
            renderPassEvent = evt;
        }

        /// <inheritdoc/>
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            // Setup Legacy XR buffer states
            if (renderingData.cameraData.xrPass.hasMultiXrView)
            {
                // Setup legacy XR stereo buffer
                Debug.Assert(renderingData.cameraData.xrPass.viewCount == 2, "View Count must be 2, other view count is not implemented yet!");
                renderingData.cameraData.camera.SetStereoProjectionMatrix(Camera.StereoscopicEye.Left, renderingData.cameraData.xrPass.GetProjMatrix(0));
                renderingData.cameraData.camera.SetStereoViewMatrix(Camera.StereoscopicEye.Left, renderingData.cameraData.xrPass.GetViewMatrix(0));
                renderingData.cameraData.camera.SetStereoProjectionMatrix(Camera.StereoscopicEye.Right, renderingData.cameraData.xrPass.GetProjMatrix(1));
                renderingData.cameraData.camera.SetStereoViewMatrix(Camera.StereoscopicEye.Right, renderingData.cameraData.xrPass.GetViewMatrix(1));

                // Use legacy stereo instancing mode to have legacy XR code path configured
                CommandBuffer cmd = CommandBufferPool.Get();
                cmd.SetSinglePassStereo(SinglePassStereoMode.Instancing);
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                // Calling into build in skybox pass
                context.DrawSkybox(renderingData.cameraData.camera);

                // Disable Legacy XR path
                cmd.SetSinglePassStereo(SinglePassStereoMode.None);

                // Reset legacy XR stereo buffer
                renderingData.cameraData.camera.ResetStereoProjectionMatrices();
                renderingData.cameraData.camera.ResetStereoViewMatrices();
                CommandBufferPool.Release(cmd);
            }
            else
            {
                context.DrawSkybox(renderingData.cameraData.camera);
            }
        }
    }
}
