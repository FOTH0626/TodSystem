using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class AtmosphereRenderFeature : ScriptableRendererFeature
{
    public AtmosphereSettings AtmosphereSettings;
    public Shader transmittanceLutShader;
    public Shader multiScatteringLutShader;
    public Shader skyViewLutShader;
    public Shader aerialPerspectiveLutShader;
    
    CustomRenderPass _pass;

    /// <inheritdoc/>
    public override void Create()
    {
        if (_pass != null)
        {
            _pass.CleanupLut();
            CoreUtils.Destroy(_pass.TransmittanceLutMat);
            CoreUtils.Destroy(_pass.MultiScatteringLutMat);
            CoreUtils.Destroy(_pass.SkyViewLutMat);
            CoreUtils.Destroy(_pass.AerialPerspectiveLutMat);
        }

        _pass = new CustomRenderPass();
        _pass.Init();
        _pass.AtmosphereSettings = AtmosphereSettings;
        _pass.TransmittanceLutMat = CoreUtils.CreateEngineMaterial(transmittanceLutShader);
        _pass.MultiScatteringLutMat = CoreUtils.CreateEngineMaterial(multiScatteringLutShader);
        _pass.SkyViewLutMat = CoreUtils.CreateEngineMaterial(skyViewLutShader);
        _pass.AerialPerspectiveLutMat = CoreUtils.CreateEngineMaterial(aerialPerspectiveLutShader);
        _pass.renderPassEvent = RenderPassEvent.BeforeRendering;
    }

    // Here you can inject one or multiple render passes in the renderer.
    // This method is called when setting up the renderer once per-camera.
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        ref var cameraData = ref renderingData.cameraData;

        if (cameraData.cameraType != CameraType.SceneView &&
            cameraData.cameraType != CameraType.Game)
        {
            return;
        }

        if (cameraData.renderType != CameraRenderType.Base)
        {
            return;
        }

        if (_pass == null)
        {
            return;
        }

        _pass.AtmosphereSettings = AtmosphereSettings;
        renderer.EnqueuePass(_pass);
    }

    protected override void Dispose(bool disposing)
    {
        _pass?.CleanupLut();
        CoreUtils.Destroy(_pass?.TransmittanceLutMat);
        CoreUtils.Destroy(_pass?.MultiScatteringLutMat);
        CoreUtils.Destroy(_pass?.SkyViewLutMat);
        CoreUtils.Destroy(_pass?.AerialPerspectiveLutMat);
        _pass = null;
    }
}