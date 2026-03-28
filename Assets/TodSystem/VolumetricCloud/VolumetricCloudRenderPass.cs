using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class VolumetricCloudRenderPass : ScriptableRenderPass
{
    private sealed class CameraHistoryState
    {
        public readonly RTHandle[] Color = new RTHandle[2];
        public readonly RTHandle[] Guide = new RTHandle[2];

        public int ReadIndex;
        public bool IsValid;
        public int Width;
        public int Height;
        public Matrix4x4 PrevViewProj;
        public int LastUsedFrame;

        public RTHandle ReadColor => Color[ReadIndex];
        public RTHandle ReadGuide => Guide[ReadIndex];
        public RTHandle WriteColor => Color[1 - ReadIndex];
        public RTHandle WriteGuide => Guide[1 - ReadIndex];

        public void EnsureAllocated(RenderTextureDescriptor descriptor, int cameraId)
        {
            bool sizeChanged = Width != descriptor.width || Height != descriptor.height;
            Width = descriptor.width;
            Height = descriptor.height;

            RenderingUtils.ReAllocateIfNeeded(ref Color[0], descriptor, FilterMode.Bilinear, TextureWrapMode.Clamp,
                name: $"_VolumetricCloudHistoryColor_{cameraId}_A");
            RenderingUtils.ReAllocateIfNeeded(ref Color[1], descriptor, FilterMode.Bilinear, TextureWrapMode.Clamp,
                name: $"_VolumetricCloudHistoryColor_{cameraId}_B");
            RenderingUtils.ReAllocateIfNeeded(ref Guide[0], descriptor, FilterMode.Bilinear, TextureWrapMode.Clamp,
                name: $"_VolumetricCloudHistoryGuide_{cameraId}_A");
            RenderingUtils.ReAllocateIfNeeded(ref Guide[1], descriptor, FilterMode.Bilinear, TextureWrapMode.Clamp,
                name: $"_VolumetricCloudHistoryGuide_{cameraId}_B");

            if (sizeChanged)
            {
                Invalidate();
            }
        }

        public void Swap()
        {
            ReadIndex = 1 - ReadIndex;
            IsValid = true;
        }

        public void Invalidate()
        {
            IsValid = false;
        }

        public void Release()
        {
            for (int i = 0; i < Color.Length; ++i)
            {
                Color[i]?.Release();
                Color[i] = null;
                Guide[i]?.Release();
                Guide[i] = null;
            }

            IsValid = false;
            ReadIndex = 0;
            Width = 0;
            Height = 0;
        }
    }

    private static readonly Vector2[] BlueNoiseOffsets =
    {
        new Vector2(0.0f, 0.0f),
        new Vector2(17.0f, 29.0f),
        new Vector2(31.0f, 7.0f),
        new Vector2(47.0f, 43.0f),
        new Vector2(9.0f, 53.0f),
        new Vector2(25.0f, 15.0f),
        new Vector2(39.0f, 61.0f),
        new Vector2(55.0f, 21.0f)
    };

    private Material _volumetricCloudMaterial;
    private RTHandle _cameraColor;
    private RTHandle _cloudRawColor;
    private RTHandle _cloudRawGuide;
    private VolumetricCloudVolume _volume;
    private Texture _blueNoiseTexture;

    private readonly Dictionary<int, CameraHistoryState> _cameraHistoryStates = new();
    private readonly List<int> _staleCameraIds = new();
    private readonly RenderTargetIdentifier[] _rawTargets = new RenderTargetIdentifier[2];
    private readonly RenderTargetIdentifier[] _historyTargets = new RenderTargetIdentifier[2];
    private readonly ProfilingSampler _sampler = new("VolumetricCloud");

    private const int RenderCloudPassIndex = 0;
    private const int TemporalAccumulatePassIndex = 1;
    private const int ResolveCompositePassIndex = 2;
    private const int HistoryLifetimeFrames = 120;

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
    private static readonly int AmbientStrength = Shader.PropertyToID("_AmbientStrength");
    private static readonly int SkyAmbientStrength = Shader.PropertyToID("_SkyAmbientStrength");
    private static readonly int MultiScatteringStrength = Shader.PropertyToID("_MultiScatteringStrength");
    private static readonly int PowderSterngth = Shader.PropertyToID("_PowderSterngth");
    private static readonly int CloudAmbientColor = Shader.PropertyToID("_CloudAmbientColor");
    private static readonly int CloudTopOffsetDirection = Shader.PropertyToID("_CloudTopOffsetDirection");
    private static readonly int CloudTopOffsetDistance = Shader.PropertyToID("_CloudTopOffsetDistance");
    private static readonly int CloudTopShapeBlendStart = Shader.PropertyToID("_CloudTopShapeBlendStart");
    private static readonly int CloudTopShapeBlendEnd = Shader.PropertyToID("_CloudTopShapeBlendEnd");
    private static readonly int TemporalHistoryWeight = Shader.PropertyToID("_TemporalHistoryWeight");
    private static readonly int TemporalWorldPosThreshold = Shader.PropertyToID("_TemporalWorldPosThreshold");
    private static readonly int TemporalOpacityThreshold = Shader.PropertyToID("_TemporalOpacityThreshold");
    private static readonly int TemporalLuminanceThreshold = Shader.PropertyToID("_TemporalLuminanceThreshold");
    private static readonly int TemporalClampStrength = Shader.PropertyToID("_TemporalClampStrength");

    private static readonly int CloudBlueNoiseTexture = Shader.PropertyToID("_CloudBlueNoiseTexture");
    private static readonly int CloudBlueNoiseParams = Shader.PropertyToID("_CloudBlueNoiseParams");
    private static readonly int CloudHalfResolutionSize = Shader.PropertyToID("_CloudHalfResolutionSize");
    private static readonly int CloudCurrentColorTexture = Shader.PropertyToID("_CloudCurrentColorTexture");
    private static readonly int CloudCurrentGuideTexture = Shader.PropertyToID("_CloudCurrentGuideTexture");
    private static readonly int CloudHistoryColorTexture = Shader.PropertyToID("_CloudHistoryColorTexture");
    private static readonly int CloudHistoryGuideTexture = Shader.PropertyToID("_CloudHistoryGuideTexture");
    private static readonly int CloudResolvedColorTexture = Shader.PropertyToID("_CloudResolvedColorTexture");
    private static readonly int CloudPrevViewProj = Shader.PropertyToID("_CloudPrevViewProj");
    private static readonly int CloudHistoryValid = Shader.PropertyToID("_CloudHistoryValid");

    #endregion

    public VolumetricCloudRenderPass(Material mat, VolumetricCloudVolume volume)
    {
        _volumetricCloudMaterial = mat;
        _volume = volume;
        ConfigureInput(ScriptableRenderPassInput.Depth);
    }

    public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
    {
        RenderTextureDescriptor halfResolutionDescriptor = BuildHalfResolutionDescriptor(cameraTextureDescriptor);

        RenderingUtils.ReAllocateIfNeeded(ref _cloudRawColor, halfResolutionDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp,
            name: "_VolumetricCloudRawColor");
        RenderingUtils.ReAllocateIfNeeded(ref _cloudRawGuide, halfResolutionDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp,
            name: "_VolumetricCloudRawGuide");
    }

    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        if (_volumetricCloudMaterial == null ||
            _volume == null ||
            _cameraColor == null ||
            _cloudRawColor == null ||
            _cloudRawGuide == null)
        {
            return;
        }

        int currentFrame = Time.frameCount;
        CleanupStaleHistory(currentFrame);

        RenderTextureDescriptor halfResolutionDescriptor = BuildHalfResolutionDescriptor(renderingData.cameraData.cameraTargetDescriptor);


        Texture blueNoiseTexture = GetBlueNoiseTexture();
        Camera camera = renderingData.cameraData.camera;
        int cameraId = camera.GetInstanceID();
        Matrix4x4 currentViewProj = renderingData.cameraData.GetGPUProjectionMatrixNoJitter() * renderingData.cameraData.GetViewMatrix();

        var commandBuffer = CommandBufferPool.Get("VolumetricCloud");
        using (new ProfilingScope(commandBuffer, _sampler))
        {
            UpdateMaterialParameters();
            UploadFrameParameters(commandBuffer, halfResolutionDescriptor, blueNoiseTexture, currentFrame);

            _rawTargets[0] = _cloudRawColor.nameID;
            _rawTargets[1] = _cloudRawGuide.nameID;
            CoreUtils.SetRenderTarget(commandBuffer, _rawTargets, _cloudRawColor, ClearFlag.Color, Color.clear);
            CoreUtils.DrawFullScreen(commandBuffer, _volumetricCloudMaterial, shaderPassId: RenderCloudPassIndex);

            RTHandle resolveColor = _cloudRawColor;

            if (_volume.EnableTemporalAccumulation.value)
            {
                CameraHistoryState historyState = GetOrCreateHistoryState(cameraId, currentFrame);
                historyState.EnsureAllocated(halfResolutionDescriptor, cameraId);

                commandBuffer.SetGlobalTexture(CloudCurrentColorTexture, _cloudRawColor.nameID);
                commandBuffer.SetGlobalTexture(CloudCurrentGuideTexture, _cloudRawGuide.nameID);
                commandBuffer.SetGlobalTexture(CloudHistoryColorTexture, historyState.ReadColor.nameID);
                commandBuffer.SetGlobalTexture(CloudHistoryGuideTexture, historyState.ReadGuide.nameID);
                commandBuffer.SetGlobalMatrix(CloudPrevViewProj, historyState.PrevViewProj);
                commandBuffer.SetGlobalFloat(CloudHistoryValid, historyState.IsValid ? 1.0f : 0.0f);

                _historyTargets[0] = historyState.WriteColor.nameID;
                _historyTargets[1] = historyState.WriteGuide.nameID;
                CoreUtils.SetRenderTarget(commandBuffer, _historyTargets, historyState.WriteColor, ClearFlag.None, Color.clear);
                CoreUtils.DrawFullScreen(commandBuffer, _volumetricCloudMaterial, shaderPassId: TemporalAccumulatePassIndex);

                historyState.Swap();
                historyState.PrevViewProj = currentViewProj;
                historyState.LastUsedFrame = currentFrame;

                resolveColor = historyState.ReadColor;
            }
            else if (_cameraHistoryStates.TryGetValue(cameraId, out CameraHistoryState historyState))
            {
                historyState.Invalidate();
                historyState.PrevViewProj = currentViewProj;
                historyState.LastUsedFrame = currentFrame;
            }

            commandBuffer.SetGlobalTexture(CloudResolvedColorTexture, resolveColor.nameID);
            CoreUtils.SetRenderTarget(commandBuffer, _cameraColor, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, ClearFlag.None, Color.clear);
            CoreUtils.DrawFullScreen(commandBuffer, _volumetricCloudMaterial, shaderPassId: ResolveCompositePassIndex);
        }

        context.ExecuteCommandBuffer(commandBuffer);
        commandBuffer.Clear();
        CommandBufferPool.Release(commandBuffer);
    }

    public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
    {
    }

    public override void OnCameraCleanup(CommandBuffer cmd)
    {
    }

    private void UpdateMaterialParameters()
    {
        _volumetricCloudMaterial.SetFloat(CloudMapSize, _volume.CloudMapSize.value);
        _volumetricCloudMaterial.SetFloat(CloudLayerLowHeight, _volume.CloudLayerLowHeight.value);
        _volumetricCloudMaterial.SetFloat(CloudLayerHighHeight, _volume.CloudLayerHighHeight.value);
        _volumetricCloudMaterial.SetInt(RayMarchingSteps, _volume.RayMarchingSteps.value);
        _volumetricCloudMaterial.SetFloat(ErosionStrength, _volume.ErosionStrength.value);
        _volumetricCloudMaterial.SetFloat(IsRainyCloud, _volume.IsRainyCloud.value);
        _volumetricCloudMaterial.SetFloat(WeatherMapScale, _volume.WeatherMapScale.value);
        _volumetricCloudMaterial.SetFloat(ShapeNoiseScale, _volume.ShapeNoiseScale.value);
        _volumetricCloudMaterial.SetFloat(DetailNoiseScale, _volume.DetailNoiseScale.value);
        _volumetricCloudMaterial.SetFloat(AmbientStrength, _volume.AmbientStrength.value);
        _volumetricCloudMaterial.SetFloat(SkyAmbientStrength, _volume.SkyAmbientStrength.value);
        _volumetricCloudMaterial.SetFloat(MultiScatteringStrength, _volume.MultiScatteringStrength.value);
        _volumetricCloudMaterial.SetFloat(PowderSterngth, _volume.PowderStrength.value);
        _volumetricCloudMaterial.SetVector(CloudTopOffsetDirection, new Vector4(
            _volume.TopOffsetDirection.value.x,
            _volume.TopOffsetDirection.value.y,
            0.0f,
            0.0f));
        _volumetricCloudMaterial.SetFloat(CloudTopOffsetDistance, _volume.TopOffsetDistance.value);
        _volumetricCloudMaterial.SetFloat(CloudTopShapeBlendStart, _volume.TopShapeBlendStart.value);
        _volumetricCloudMaterial.SetFloat(CloudTopShapeBlendEnd, _volume.TopShapeBlendEnd.value);
        _volumetricCloudMaterial.SetFloat(TemporalHistoryWeight, _volume.TemporalHistoryWeight.value);
        _volumetricCloudMaterial.SetFloat(TemporalWorldPosThreshold, _volume.TemporalWorldPosThreshold.value);
        _volumetricCloudMaterial.SetFloat(TemporalOpacityThreshold, _volume.TemporalOpacityThreshold.value);
        _volumetricCloudMaterial.SetFloat(TemporalLuminanceThreshold, _volume.TemporalLuminanceThreshold.value);
        _volumetricCloudMaterial.SetFloat(TemporalClampStrength, _volume.TemporalClampStrength.value);

        Color ambientColor = (
            RenderSettings.ambientSkyColor * 0.55f +
            RenderSettings.ambientEquatorColor * 0.35f +
            RenderSettings.ambientGroundColor * 0.10f) * RenderSettings.ambientIntensity;
        _volumetricCloudMaterial.SetColor(CloudAmbientColor, ambientColor);
    }

    private void UploadFrameParameters(CommandBuffer commandBuffer, RenderTextureDescriptor descriptor, Texture blueNoiseTexture, int currentFrame)
    {
        float width = descriptor.width;
        float height = descriptor.height;
        Vector2 blueNoiseDimensions = new Vector2(
            Mathf.Max(1.0f, blueNoiseTexture.width),
            Mathf.Max(1.0f, blueNoiseTexture.height));
        Vector2 offset = BlueNoiseOffsets[currentFrame & 7];

        commandBuffer.SetGlobalTexture(CloudBlueNoiseTexture, blueNoiseTexture);
        commandBuffer.SetGlobalVector(CloudBlueNoiseParams, new Vector4(
            width / blueNoiseDimensions.x,
            height / blueNoiseDimensions.y,
            Mathf.Repeat(offset.x, blueNoiseDimensions.x) / blueNoiseDimensions.x,
            Mathf.Repeat(offset.y, blueNoiseDimensions.y) / blueNoiseDimensions.y));
        commandBuffer.SetGlobalVector(CloudHalfResolutionSize, new Vector4(width, height, 1.0f / width, 1.0f / height));
    }

    private Texture GetBlueNoiseTexture()
    {
        if (_blueNoiseTexture != null)
        {
            return _blueNoiseTexture;
        }

        UniversalRenderPipelineAsset pipelineAsset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        pipelineAsset ??= QualitySettings.renderPipeline as UniversalRenderPipelineAsset;

        _blueNoiseTexture = pipelineAsset?.textures?.blueNoise64LTex;
        if (_blueNoiseTexture == null)
        {
            _blueNoiseTexture = Texture2D.grayTexture;
        }

        return _blueNoiseTexture;
    }

    private CameraHistoryState GetOrCreateHistoryState(int cameraId, int currentFrame)
    {
        if (!_cameraHistoryStates.TryGetValue(cameraId, out CameraHistoryState historyState))
        {
            historyState = new CameraHistoryState
            {
                LastUsedFrame = currentFrame
            };
            _cameraHistoryStates.Add(cameraId, historyState);
        }

        return historyState;
    }

    private void CleanupStaleHistory(int currentFrame)
    {
        _staleCameraIds.Clear();

        foreach (KeyValuePair<int, CameraHistoryState> historyStatePair in _cameraHistoryStates)
        {
            if (currentFrame - historyStatePair.Value.LastUsedFrame > HistoryLifetimeFrames)
            {
                _staleCameraIds.Add(historyStatePair.Key);
            }
        }

        foreach (int staleCameraId in _staleCameraIds)
        {
            _cameraHistoryStates[staleCameraId].Release();
            _cameraHistoryStates.Remove(staleCameraId);
        }
    }

    private static RenderTextureDescriptor BuildHalfResolutionDescriptor(RenderTextureDescriptor cameraTextureDescriptor)
    {
        RenderTextureDescriptor descriptor = cameraTextureDescriptor;
        descriptor.width = Mathf.Max(1, (cameraTextureDescriptor.width + 1) / 2);
        descriptor.height = Mathf.Max(1, (cameraTextureDescriptor.height + 1) / 2);
        descriptor.msaaSamples = 1;
        descriptor.depthBufferBits = 0;
        descriptor.graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat;
        descriptor.useMipMap = false;
        descriptor.autoGenerateMips = false;
        return descriptor;
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

    public void ResetCameraHistory(Camera camera)
    {
        if (camera == null)
        {
            return;
        }

        int cameraId = camera.GetInstanceID();
        if (_cameraHistoryStates.TryGetValue(cameraId, out CameraHistoryState historyState))
        {
            historyState.Release();
            _cameraHistoryStates.Remove(cameraId);
        }
    }

    public void Dispose()
    {
        _cloudRawColor?.Release();
        _cloudRawColor = null;
        _cloudRawGuide?.Release();
        _cloudRawGuide = null;

        foreach (CameraHistoryState historyState in _cameraHistoryStates.Values)
        {
            historyState.Release();
        }

        _cameraHistoryStates.Clear();
        _staleCameraIds.Clear();
    }
}
