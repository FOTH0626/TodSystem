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
    
    class CustomRenderPass : ScriptableRenderPass
    {
        
        public AtmosphereSettings AtmosphereSettings;
        public Material TransmittanceLutMat;
        public Material MultiScatteringLutMat;
        public Material SkyViewLutMat;

        private RTHandle _transmittanceLut;
        private RTHandle _multiScatteringLut;
        private RTHandle _skyViewLut;
        
        //hash 
        private bool _LUTsHasBuilt;
        private int _lastSettingsHash;


        private static readonly int _transmittanceLutID = Shader.PropertyToID("_transmittanceLut");
        private static readonly int _multiScatteringLutID = Shader.PropertyToID("_multiScatteringLut");
        private static readonly int _skyViewLutID = Shader.PropertyToID("_skyViewLut");
        
        private static readonly int _SeaLevelId = Shader.PropertyToID("_SeaLevel");
        private static readonly int _PlanetRadiusId = Shader.PropertyToID("_PlanetRadius");
        private static readonly int _AtmosphereHeightId = Shader.PropertyToID("_AtmosphereHeight");
        private static readonly int _SunLightIntensityId = Shader.PropertyToID("_SunLightIntensity");
        private static readonly int _SunLightColorId = Shader.PropertyToID("_SunLightColor");
        private static readonly int _SunDiskAngleId = Shader.PropertyToID("_SunDiskAngle");
        private static readonly int _RayleighScatteringScaleId = Shader.PropertyToID("_RayleighScatteringScale");
        private static readonly int _RayleighScatteringScalarHeightId = Shader.PropertyToID("_RayleighScatteringScalarHeight");
        private static readonly int _MieScatteringScaleId = Shader.PropertyToID("_MieScatteringScale");
        private static readonly int _MieAnisotropyId = Shader.PropertyToID("_MieAnisotropy");
        private static readonly int _MieScatteringScalarHeightId = Shader.PropertyToID("_MieScatteringScalarHeight");
        private static readonly int _OzoneAbsorptionScaleId = Shader.PropertyToID("_OzoneAbsorptionScale");
        private static readonly int _OzoneLevelCenterHeightId = Shader.PropertyToID("_OzoneLevelCenterHeight");
        private static readonly int _OzoneLevelWidthId = Shader.PropertyToID("_OzoneLevelWidth");
        private static readonly int _AerialPerspectiveDistanceId = Shader.PropertyToID("_AerialPerspectiveDistance");

        private static readonly ProfilingSampler _ExecuteSampler = new ProfilingSampler("AtmosphereLUT.Execute");
        private static readonly ProfilingSampler _UploadParamsSampler = new ProfilingSampler("AtmosphereLUT.UploadParams");
        private static readonly ProfilingSampler _RedrawLutsSampler = new ProfilingSampler("AtmosphereLUT.RedrawLUTs");
        
        
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
            var cmd = CommandBufferPool.Get("Atmosphere Lut");

            using (new ProfilingScope(cmd, _ExecuteSampler))
            {
                UploadAtmosphereParams(cmd);

                int currentHash = ComputeSettingsHash();
                if (!_LUTsHasBuilt || currentHash != _lastSettingsHash) 
                {
                    RedrawLuts(cmd);
                    _lastSettingsHash = currentHash;
                }

                cmd.SetGlobalTexture(_transmittanceLutID, _transmittanceLut.nameID);
                cmd.SetGlobalTexture(_multiScatteringLutID, _multiScatteringLut.nameID);
                cmd.SetGlobalTexture(_skyViewLutID, _skyViewLut.nameID);
            }

            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            CommandBufferPool.Release(cmd);
        }

        // Cleanup any allocated resources that were created during the execution of this render pass.
        public override void OnCameraCleanup(CommandBuffer cmd)
        {

        }
        
        public void Init()
        {
            _transmittanceLut ??= RTHandles.Alloc(256, 64, 1,
                DepthBits.None,
                GraphicsFormat.R16G16B16A16_SFloat,
                FilterMode.Bilinear, TextureWrapMode.Clamp,
                name: "_transmittanceLut");

            _multiScatteringLut ??= RTHandles.Alloc(32, 32, 1,
                DepthBits.None, 
                GraphicsFormat.R16G16B16A16_SFloat,
                FilterMode.Bilinear, 
                TextureWrapMode.Clamp,
                name : "_multiScatteringLut");

            _skyViewLut ??= RTHandles.Alloc(256, 128, 1,
                DepthBits.None,
                GraphicsFormat.R16G16B16A16_SFloat,
                FilterMode.Bilinear,
                TextureWrapMode.Clamp,
                name: "_skyViewLut");

            _LUTsHasBuilt = false;
            _lastSettingsHash = 0;
        }
        
        private int ComputeSettingsHash()
        {
            if (AtmosphereSettings == null)
                return 0;

            unchecked
            {
                int h = 17;
                var a = AtmosphereSettings;

                h = h * 23 + a.seaLevel.GetHashCode();
                h = h * 23 + a.planetRadius.GetHashCode();
                h = h * 23 + a.atmosphereHeight.GetHashCode();
                h = h * 23 + a.sunLightIntensity.GetHashCode();
                h = h * 23 + a.sunLightColor.GetHashCode();
                h = h * 23 + a.sunDiskAngle.GetHashCode();

                h = h * 23 + a.rayleighScatteringScale.GetHashCode();
                h = h * 23 + a.rayleighScatteringScalarHeight.GetHashCode();

                h = h * 23 + a.mieScatteringScale.GetHashCode();
                h = h * 23 + a.mieAnisotropy.GetHashCode();
                h = h * 23 + a.mieScatteringScalarHeight.GetHashCode();

                h = h * 23 + a.ozoneAbsorptionScale.GetHashCode();
                h = h * 23 + a.ozoneLevelCenterHeight.GetHashCode();
                h = h * 23 + a.ozoneLevelWidth.GetHashCode();

                h = h * 23 + a.aerialPerspectiveDistance.GetHashCode();
                

                return h;
            }
        }

        private void RedrawLuts(CommandBuffer cmd)
        {
            using (new ProfilingScope(cmd, _RedrawLutsSampler))
            {
                CoreUtils.SetRenderTarget(cmd, _transmittanceLut, ClearFlag.None, Color.clear);
                CoreUtils.DrawFullScreen(cmd, TransmittanceLutMat);
                CoreUtils.SetRenderTarget(cmd, _multiScatteringLut, ClearFlag.None, Color.clear);
                CoreUtils.DrawFullScreen(cmd, MultiScatteringLutMat);
                cmd.SetGlobalTexture(_transmittanceLutID, _transmittanceLut.nameID);
                cmd.SetGlobalTexture(_multiScatteringLutID, _multiScatteringLut.nameID);

                CoreUtils.SetRenderTarget(cmd, _skyViewLut, ClearFlag.None, Color.clear);
                CoreUtils.DrawFullScreen(cmd, SkyViewLutMat);

                _LUTsHasBuilt = true;
            }
        }
        
        
        private void UploadAtmosphereParams(CommandBuffer cmd)
        {
            using (new ProfilingScope(cmd, _UploadParamsSampler))
            {
                var a = AtmosphereSettings;

                cmd.SetGlobalFloat(_SeaLevelId, a.seaLevel);
                cmd.SetGlobalFloat(_PlanetRadiusId, a.planetRadius);
                cmd.SetGlobalFloat(_AtmosphereHeightId, a.atmosphereHeight);
                cmd.SetGlobalFloat(_SunLightIntensityId, a.sunLightIntensity);
                cmd.SetGlobalColor(_SunLightColorId, a.sunLightColor);
                cmd.SetGlobalFloat(_SunDiskAngleId, a.sunDiskAngle);

                cmd.SetGlobalFloat(_RayleighScatteringScaleId, a.rayleighScatteringScale);
                cmd.SetGlobalFloat(_RayleighScatteringScalarHeightId, a.rayleighScatteringScalarHeight);

                cmd.SetGlobalFloat(_MieScatteringScaleId, a.mieScatteringScale);
                cmd.SetGlobalFloat(_MieAnisotropyId, a.mieAnisotropy);
                cmd.SetGlobalFloat(_MieScatteringScalarHeightId, a.mieScatteringScalarHeight);

                cmd.SetGlobalFloat(_OzoneAbsorptionScaleId, a.ozoneAbsorptionScale);
                cmd.SetGlobalFloat(_OzoneLevelCenterHeightId, a.ozoneLevelCenterHeight);
                cmd.SetGlobalFloat(_OzoneLevelWidthId, a.ozoneLevelWidth);

                cmd.SetGlobalFloat(_AerialPerspectiveDistanceId, a.aerialPerspectiveDistance);
            }
        }

        public void CleanupLut()
        {
            _transmittanceLut?.Release();
            _transmittanceLut = null;
            _multiScatteringLut?.Release();
            _multiScatteringLut = null;
            _skyViewLut?.Release();
            _skyViewLut = null;
        }
    }

    CustomRenderPass m_ScriptablePass;

    /// <inheritdoc/>
    public override void Create()
    {
        if (m_ScriptablePass != null)
        {
            m_ScriptablePass.CleanupLut();
            CoreUtils.Destroy(m_ScriptablePass.TransmittanceLutMat);
            CoreUtils.Destroy(m_ScriptablePass.MultiScatteringLutMat);
            CoreUtils.Destroy(m_ScriptablePass.SkyViewLutMat);
        }

        m_ScriptablePass = new CustomRenderPass();
        m_ScriptablePass.Init();
        m_ScriptablePass.AtmosphereSettings = AtmosphereSettings;
        m_ScriptablePass.TransmittanceLutMat = CoreUtils.CreateEngineMaterial(transmittanceLutShader);
        m_ScriptablePass.MultiScatteringLutMat = CoreUtils.CreateEngineMaterial(multiScatteringLutShader);
        m_ScriptablePass.SkyViewLutMat = CoreUtils.CreateEngineMaterial(skyViewLutShader);
        m_ScriptablePass.renderPassEvent = RenderPassEvent.BeforeRendering;

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
        if (m_ScriptablePass == null)
        {
            return;
        }
        renderer.EnqueuePass(m_ScriptablePass);
    }

    protected override void Dispose(bool disposing)
    {
        m_ScriptablePass?.CleanupLut();
        CoreUtils.Destroy(m_ScriptablePass?.TransmittanceLutMat);
        CoreUtils.Destroy(m_ScriptablePass?.MultiScatteringLutMat);
        CoreUtils.Destroy(m_ScriptablePass?.SkyViewLutMat);
        m_ScriptablePass = null;
    }
}


