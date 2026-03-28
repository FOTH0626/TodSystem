using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class VolumetricCloudRenderFeature : ScriptableRendererFeature
{
    private const string VolumetricCloudShaderName = "TOD/VolumetricCloud/VolumetricCloud";

    public Material volumetricCloudMat;

    private VolumetricCloudRenderPass _pass;
    private VolumetricCloudVolume _volume;

    public override void Create()
    {
        if (volumetricCloudMat == null)
        {
            Debug.LogWarning("Volumetric Cloud Material is null");
            return;
        }
        _volume = VolumeManager.instance.stack?.GetComponent<VolumetricCloudVolume>();

        _pass ??= new VolumetricCloudRenderPass(volumetricCloudMat, _volume)
        {
            renderPassEvent = RenderPassEvent.AfterRenderingSkybox
        };
        _pass.Set(volumetricCloudMat, _volume);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (_pass == null)
        {
            return;
        }

        if (renderingData.cameraData.cameraType != CameraType.Game && renderingData.cameraData.cameraType != CameraType.SceneView)
        {
            return;
        }

        if (renderingData.cameraData.renderType != CameraRenderType.Base)
        {
            return;
        }

        _volume = VolumeManager.instance.stack?.GetComponent<VolumetricCloudVolume>();
        if (_volume == null || !_volume.IsActive())
        {
            return;
        }

        _pass.Set(volumetricCloudMat, _volume);

        renderer.EnqueuePass(_pass);
    }

    public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData)
    {
        _pass?.SetTarget(renderer.cameraColorTargetHandle);
    }

    protected override void Dispose(bool disposing)
    {
        _pass?.Dispose();
        _pass = null;
    }

 
}
