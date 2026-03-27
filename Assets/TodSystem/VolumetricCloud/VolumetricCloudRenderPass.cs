using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class VolumetricCloudRenderPass : ScriptableRenderPass
{
    private Material _volumetricCloudMaterial;
    private RTHandle _cameraColor;
    private RTHandle _tempColor;
    private VolumetricCloudVolume _volume;

    private readonly ProfilingSampler _sampler = new("VolumetricCloud");

    #region parameters

    private static readonly int CloudMapSize = Shader.PropertyToID("_CloudMapSize");
    private static readonly int CloudLayerLowHeight = Shader.PropertyToID("_CloudLayerLowHeight");
    private static readonly int CloudLayerHighHeight = Shader.PropertyToID("_CloudLayerHighHeight");
    private static readonly int RayMarchingSteps = Shader.PropertyToID("_RayMarchingSteps");
    private static readonly int ErosionStrength = Shader.PropertyToID("_ErosionStrength");
    private static readonly int IsRainyCloud = Shader.PropertyToID("_isRainyCloud");
    private static readonly int WeatherMapScale = Shader.PropertyToID("_WeatherMapScale");
    private static readonly int ShapeNoiseScale = Shader.PropertyToID("_ShapeNoiseScale");
    private static readonly int DetailNoiseScale = Shader.PropertyToID("_DetailNoiseScale");
    
    #endregion
    
    public VolumetricCloudRenderPass(Material mat, VolumetricCloudVolume volume)
    {
        _volumetricCloudMaterial = mat;
        _volume = volume;
        ConfigureInput(ScriptableRenderPassInput.Depth);
    }

    public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
    {
        var desc = cameraTextureDescriptor;
        desc.msaaSamples = 1;
        desc.depthBufferBits = 0;

        RenderingUtils.ReAllocateIfNeeded(ref _tempColor, desc, FilterMode.Bilinear, TextureWrapMode.Clamp,
            name: "_VolumetricCloudTemp");

    }

    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        if (_volumetricCloudMaterial == null || _volume == null || _cameraColor == null || _tempColor == null)
            return;

        var cmdbuffer = CommandBufferPool.Get("VolumetricCloud");
        using (new ProfilingScope(cmdbuffer, _sampler))
        {
            UpdateMaterialParameters();
            Blitter.BlitCameraTexture(cmdbuffer,_cameraColor,_tempColor,_volumetricCloudMaterial,0);
            Blitter.BlitCameraTexture(cmdbuffer,_tempColor,_cameraColor);
        }
        context.ExecuteCommandBuffer(cmdbuffer);
        cmdbuffer.Clear();
        CommandBufferPool.Release(cmdbuffer);
        
    }

    public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
    {
        
    }

    public override void OnCameraCleanup(CommandBuffer cmd)
    {
        
    }

    private void UpdateMaterialParameters()
    {
        _volumetricCloudMaterial.SetFloat(CloudMapSize,_volume.CloudMapSize.value);
        _volumetricCloudMaterial.SetFloat(CloudLayerLowHeight, _volume.CloudLayerLowHeight.value);
        _volumetricCloudMaterial.SetFloat(CloudLayerHighHeight, _volume.CloudLayerHighHeight.value);
        _volumetricCloudMaterial.SetInt(RayMarchingSteps, _volume.RayMarchingSteps.value);
        _volumetricCloudMaterial.SetFloat(ErosionStrength, _volume.ErosionStrength.value);
        _volumetricCloudMaterial.SetFloat(IsRainyCloud,_volume.IsRainyCloud.value);
        _volumetricCloudMaterial.SetFloat(WeatherMapScale, _volume.WeatherMapScale.value);
        _volumetricCloudMaterial.SetFloat(ShapeNoiseScale,_volume.ShapeNoiseScale.value);
        _volumetricCloudMaterial.SetFloat(DetailNoiseScale,_volume.DetailNoiseScale.value);
    }

    public void SetTarget(RTHandle cameraColorHandle)
    {
        _cameraColor = cameraColorHandle;
    }

    public void Set(Material mat, VolumetricCloudVolume volume)
    {
        _volumetricCloudMaterial = mat;
        _volume = volume;
    }

    public void Dispose()
    {
        _tempColor.Release();
        // _cameraColor.Release();
    }

}
