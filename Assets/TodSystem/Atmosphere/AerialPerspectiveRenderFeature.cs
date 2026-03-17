using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class AerialPerspectiveRenderFeature : ScriptableRendererFeature
{
    
    class CustomRenderPass : ScriptableRenderPass
    {

        public Material AerialPerspectiveMaterial;

        private RTHandle _cameraColor;
        private RTHandle _tempColor;

        private readonly ProfilingSampler _sampler = new("AerialPerspective");

        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            var desc = cameraTextureDescriptor;
            desc.depthBufferBits = 0;
            desc.msaaSamples = 1;

            RenderingUtils.ReAllocateIfNeeded(
                ref _tempColor,
                desc,
                FilterMode.Bilinear,
                TextureWrapMode.Clamp,
                name: "_AerialPerspectiveTemp");
        }

        // This method is called before executing the render pass.
        // It can be used to configure render targets and their clear state. Also to create temporary render target textures.
        // When empty this render pass will render to the active camera render target.
        // You should never call CommandBuffer.SetRenderTarget. Instead call <c>ConfigureTarget</c> and <c>ConfigureClear</c>.
        // The render pipeline will ensure target setup and clearing happens in a performant manner.
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
        }

        // Here you can implement the rendering logic.
        // Use <c>ScriptableRenderContext</c> to issue drawing commands or execute command buffers
        // https://docs.unity3d.com/ScriptReference/Rendering.ScriptableRenderContext.html
        // You don't have to call ScriptableRenderContext.submit, the render pipeline will call it at specific points in the pipeline.
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (renderingData.cameraData.cameraType != CameraType.Game &&
                renderingData.cameraData.cameraType != CameraType.SceneView )
            {
                return;
            }
            
            var cmdbuffer = CommandBufferPool.Get();
            using (new ProfilingScope(cmdbuffer, _sampler))
            {
                Blitter.BlitCameraTexture(cmdbuffer, _cameraColor,_tempColor, AerialPerspectiveMaterial, 0);
                Blitter.BlitCameraTexture(cmdbuffer, _tempColor, _cameraColor);
            }
            context.ExecuteCommandBuffer(cmdbuffer);
            cmdbuffer.Clear();
            CommandBufferPool.Release(cmdbuffer);
        }

        // Cleanup any allocated resources that were created during the execution of this render pass.
        public override void OnCameraCleanup(CommandBuffer cmd)
        {
        }

        public void SetTarget(RTHandle cameraColorHandle)
        {
            _cameraColor = cameraColorHandle;
        }

        public void Dispose()
        {
            _tempColor?.Release();
            _tempColor = null;
        }
        
    }

    CustomRenderPass m_ScriptablePass;

    public Shader aerialPerspectiveShader;
    /// <inheritdoc/>
    public override void Create()
    {
        m_ScriptablePass = new CustomRenderPass();

        m_ScriptablePass.AerialPerspectiveMaterial = CoreUtils.CreateEngineMaterial(aerialPerspectiveShader);

        // Configures where the render pass should be injected.
        m_ScriptablePass.renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
    }

    public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData)
    {
        var cameraType = renderingData.cameraData.cameraType;
        if (cameraType == CameraType.Game ||
            cameraType == CameraType.SceneView )
        {
            // m_ScriptablePass.ConfigureInput(ScriptableRenderPassInput.Color);
            m_ScriptablePass.ConfigureInput(ScriptableRenderPassInput.Depth);
            m_ScriptablePass.SetTarget(renderer.cameraColorTargetHandle);
        }
    }

    // Here you can inject one or multiple render passes in the renderer.
    // This method is called when setting up the renderer once per-camera.
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        var cameraType = renderingData.cameraData.cameraType;
        if (cameraType != CameraType.Game &&
            cameraType != CameraType.SceneView)
            return;
        
        renderer.EnqueuePass(m_ScriptablePass);
    }

    protected override void Dispose(bool disposing)
    {
        CoreUtils.Destroy(m_ScriptablePass?.AerialPerspectiveMaterial);
        m_ScriptablePass?.Dispose();
        m_ScriptablePass = null;
    }
}


