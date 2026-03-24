using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class VolumetricCloudRenderFeature : ScriptableRendererFeature
{
    private const string VolumetricCloudShaderName = "TOD/VolumetricCloud/VolumetricCloud";

    public Material volumetricCloudMat;

    private VolumetricCloudRenderPass _pass;
    private VolumetricCloudVolume _volume;
    private bool _ownsRuntimeMaterial;
    private bool _loggedMissingMaterial;
    private bool _loggedMissingVolume;

    public override void Create()
    {
        if (!TryEnsureDependencies())
            return;

        _pass ??= new VolumetricCloudRenderPass(volumetricCloudMat, _volume)
        {
            renderPassEvent = RenderPassEvent.AfterRenderingSkybox
        };

        _pass.Set(volumetricCloudMat, _volume);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (_pass == null)
            Create();
        if (_pass == null)
            return;

        renderer.EnqueuePass(_pass);
    }

    public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData)
    {
        if (_pass == null)
            return;

        _pass.SetTarget(renderer.cameraColorTargetHandle);
    }

    protected override void Dispose(bool disposing)
    {
        if (_ownsRuntimeMaterial && volumetricCloudMat != null)
        {
            DestroyImmediate(volumetricCloudMat);
            volumetricCloudMat = null;
        }

        _pass = null;
    }

    private bool TryEnsureDependencies()
    {
        if (_volume == null && VolumeManager.instance != null && VolumeManager.instance.stack != null)
            _volume = VolumeManager.instance.stack.GetComponent<VolumetricCloudVolume>();

        if (_volume == null)
        {
            if (!_loggedMissingVolume)
            {
                Debug.LogWarning(
                    "VolumetricCloudVolume not found in the active Volume stack. Volumetric cloud pass will be skipped.");
                _loggedMissingVolume = true;
            }

            _pass = null;
            return false;
        }

        _loggedMissingVolume = false;

        if (volumetricCloudMat == null)
        {
            var shader = Shader.Find(VolumetricCloudShaderName);
            if (shader != null)
            {
                volumetricCloudMat = new Material(shader)
                {
                    name = "VolumetricCloud (Runtime)"
                };
                _ownsRuntimeMaterial = true;
            }
        }

        if (volumetricCloudMat == null)
        {
            if (!_loggedMissingMaterial)
            {
                Debug.LogError(
                    "Volumetric Material is null and shader fallback failed. Assign a material in VolumetricCloudRenderFeature.");
                _loggedMissingMaterial = true;
            }

            _pass = null;
            return false;
        }

        _loggedMissingMaterial = false;
        return true;
    }
}
