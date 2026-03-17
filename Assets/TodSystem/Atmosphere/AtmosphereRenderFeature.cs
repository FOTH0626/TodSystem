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

    class CustomRenderPass : ScriptableRenderPass
    {
        public AtmosphereSettings AtmosphereSettings;

        public Material TransmittanceLutMat;
        public Material MultiScatteringLutMat;
        public Material SkyViewLutMat;
        public Material AerialPerspectiveLutMat;

        private RTHandle _transmittanceLut;
        private RTHandle _multiScatteringLut;
        private RTHandle _skyViewLut;
        private RTHandle _aerialPerspectiveLut;

        //hash 
        private bool _staticLuTsHasBuilt;
        private int _lastSettingsHash;
        // private int _lastCameraAndLightingHash;


        private static readonly int _transmittanceLutID = Shader.PropertyToID("_transmittanceLut");
        private static readonly int _multiScatteringLutID = Shader.PropertyToID("_multiScatteringLut");
        private static readonly int _skyViewLutID = Shader.PropertyToID("_skyViewLut");
        private static readonly int _aerialPerspectiveLutID = Shader.PropertyToID("_aerialPerspectiveLut");

        private static readonly int _SeaLevelID = Shader.PropertyToID("_SeaLevel");
        private static readonly int _PlanetRadiusID = Shader.PropertyToID("_PlanetRadius");
        private static readonly int _AtmosphereHeightID = Shader.PropertyToID("_AtmosphereHeight");
        private static readonly int _SunLightIntensityID = Shader.PropertyToID("_SunLightIntensity");
        private static readonly int _SunLightColorID = Shader.PropertyToID("_SunLightColor");
        private static readonly int _SunDiskAngleID = Shader.PropertyToID("_SunDiskAngle");
        private static readonly int _RayleighScatteringScaleID = Shader.PropertyToID("_RayleighScatteringScale");

        private static readonly int _RayleighScatteringScalarHeightID = Shader.PropertyToID("_RayleighScatteringScalarHeight");

        private static readonly int _MieScatteringScaleID = Shader.PropertyToID("_MieScatteringScale");
        private static readonly int _MieAnisotropyID = Shader.PropertyToID("_MieAnisotropy");
        private static readonly int _MieScatteringScalarHeightID = Shader.PropertyToID("_MieScatteringScalarHeight");
        private static readonly int _OzoneAbsorptionScaleID = Shader.PropertyToID("_OzoneAbsorptionScale");
        private static readonly int _OzoneLevelCenterHeightID = Shader.PropertyToID("_OzoneLevelCenterHeight");
        private static readonly int _OzoneLevelWidthID = Shader.PropertyToID("_OzoneLevelWidth");
        private static readonly int _AerialPerspectiveDistanceID = Shader.PropertyToID("_AerialPerspectiveDistance");
        private static readonly int _AerialPerspectiveVoxelSizeID = Shader.PropertyToID("_AerialPerspectiveVoxelSize");

        private static readonly ProfilingSampler _ExecuteSampler = new ProfilingSampler("AtmosphereLUT.Execute");
        private static readonly ProfilingSampler _UploadParamsSampler = new ProfilingSampler("AtmosphereLUT.UploadParams");
        private static readonly ProfilingSampler _RedrawStaticLutsSampler = new ProfilingSampler("AtmosphereLUT.RedrawStaticLUTs");
        private static readonly ProfilingSampler _RedrawDynamicLutsSampler = new ProfilingSampler("AtmosphereLUT.RedrawDynamicLUTs");


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
            if (AtmosphereSettings == null ||
                TransmittanceLutMat == null ||
                MultiScatteringLutMat == null ||
                SkyViewLutMat == null ||
                AerialPerspectiveLutMat == null ||
                _transmittanceLut == null ||
                _multiScatteringLut == null ||
                _skyViewLut == null ||
                _aerialPerspectiveLut == null)
            {
                Debug.LogError("AtmosphereRenderFeature Execute failed: AtmosphereSettings、Material 或 LUT 资源未正确初始化。");
                return;
            }
            var cmdbuffer = CommandBufferPool.Get("Atmosphere Lut");

            using (new ProfilingScope(cmdbuffer, _ExecuteSampler))
            {
                UploadAtmosphereParams(cmdbuffer);

                int currentHash = ComputeSettingsHash();
                if (!_staticLuTsHasBuilt || currentHash != _lastSettingsHash)
                {
                    ReDrawStaticLuts(cmdbuffer);
                    _lastSettingsHash = currentHash;
                }

                cmdbuffer.SetGlobalTexture(_transmittanceLutID, _transmittanceLut.nameID);
                cmdbuffer.SetGlobalTexture(_multiScatteringLutID, _multiScatteringLut.nameID);
                ReDrawDynamicLuts(cmdbuffer);
                
                cmdbuffer.SetGlobalTexture(_skyViewLutID, _skyViewLut.nameID);
                cmdbuffer.SetGlobalTexture(_aerialPerspectiveLutID, _aerialPerspectiveLut.nameID);
            }

            context.ExecuteCommandBuffer(cmdbuffer);
            cmdbuffer.Clear();
            CommandBufferPool.Release(cmdbuffer);
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
                name: "_multiScatteringLut");

            _skyViewLut ??= RTHandles.Alloc(256, 128, 1,
                DepthBits.None,
                GraphicsFormat.R16G16B16A16_SFloat,
                FilterMode.Bilinear,
                TextureWrapMode.Clamp,
                name: "_skyViewLut");


            _aerialPerspectiveLut ??= RTHandles.Alloc(32 * 32, 32, 1,
                DepthBits.None,
                GraphicsFormat.R16G16B16A16_SFloat,
                FilterMode.Bilinear,
                TextureWrapMode.Clamp,
                name: "_aerialPerspectiveLut"
            );

            _staticLuTsHasBuilt = false;
            _lastSettingsHash = 0;
            // _lastCameraAndLightingHash = 0;
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
        

        private void ReDrawStaticLuts(CommandBuffer cmd)
        {
            using (new ProfilingScope(cmd, _RedrawStaticLutsSampler))
            {
                CoreUtils.SetRenderTarget(cmd, _transmittanceLut, ClearFlag.None, Color.clear);
                CoreUtils.DrawFullScreen(cmd, TransmittanceLutMat);
                CoreUtils.SetRenderTarget(cmd, _multiScatteringLut, ClearFlag.None, Color.clear);
                CoreUtils.DrawFullScreen(cmd, MultiScatteringLutMat);


                cmd.SetGlobalTexture(_transmittanceLutID, _transmittanceLut.nameID);
                cmd.SetGlobalTexture(_multiScatteringLutID, _multiScatteringLut.nameID);


                // cmd.SetGlobalTexture(_aerialPerspectiveLutID,_aerialPerspectiveLut.nameID);

                _staticLuTsHasBuilt = true;
            }
        }

        private void ReDrawDynamicLuts(CommandBuffer cmd)
        {
            using (new ProfilingScope(cmd, _RedrawDynamicLutsSampler))
            {
                
                CoreUtils.SetRenderTarget(cmd, _skyViewLut, ClearFlag.None, Color.clear);
                CoreUtils.DrawFullScreen(cmd, SkyViewLutMat);
                cmd.SetGlobalTexture(_skyViewLutID, _skyViewLut.nameID);

                CoreUtils.SetRenderTarget(cmd, _aerialPerspectiveLut, ClearFlag.None, Color.clear);
                CoreUtils.DrawFullScreen(cmd, AerialPerspectiveLutMat);
            }
        }


        private void UploadAtmosphereParams(CommandBuffer cmd)
        {
            using (new ProfilingScope(cmd, _UploadParamsSampler))
            {
                var a = AtmosphereSettings;

                cmd.SetGlobalFloat(_SeaLevelID, a.seaLevel);
                cmd.SetGlobalFloat(_PlanetRadiusID, a.planetRadius);
                cmd.SetGlobalFloat(_AtmosphereHeightID, a.atmosphereHeight);
                cmd.SetGlobalFloat(_SunLightIntensityID, a.sunLightIntensity);
                cmd.SetGlobalColor(_SunLightColorID, a.sunLightColor);
                cmd.SetGlobalFloat(_SunDiskAngleID, a.sunDiskAngle);

                cmd.SetGlobalFloat(_RayleighScatteringScaleID, a.rayleighScatteringScale);
                cmd.SetGlobalFloat(_RayleighScatteringScalarHeightID, a.rayleighScatteringScalarHeight);

                cmd.SetGlobalFloat(_MieScatteringScaleID, a.mieScatteringScale);
                cmd.SetGlobalFloat(_MieAnisotropyID, a.mieAnisotropy);
                cmd.SetGlobalFloat(_MieScatteringScalarHeightID, a.mieScatteringScalarHeight);

                cmd.SetGlobalFloat(_OzoneAbsorptionScaleID, a.ozoneAbsorptionScale);
                cmd.SetGlobalFloat(_OzoneLevelCenterHeightID, a.ozoneLevelCenterHeight);
                cmd.SetGlobalFloat(_OzoneLevelWidthID, a.ozoneLevelWidth);

                cmd.SetGlobalFloat(_AerialPerspectiveDistanceID, a.aerialPerspectiveDistance);
                cmd.SetGlobalVector(_AerialPerspectiveVoxelSizeID, new Vector4(32, 32, 32, 0));
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
            _aerialPerspectiveLut?.Release();
            _aerialPerspectiveLut = null;
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
            CoreUtils.Destroy(m_ScriptablePass.AerialPerspectiveLutMat);
        }

        m_ScriptablePass = new CustomRenderPass();
        m_ScriptablePass.Init();
        m_ScriptablePass.AtmosphereSettings = AtmosphereSettings;
        m_ScriptablePass.TransmittanceLutMat = CoreUtils.CreateEngineMaterial(transmittanceLutShader);
        m_ScriptablePass.MultiScatteringLutMat = CoreUtils.CreateEngineMaterial(multiScatteringLutShader);
        m_ScriptablePass.SkyViewLutMat = CoreUtils.CreateEngineMaterial(skyViewLutShader);
        m_ScriptablePass.AerialPerspectiveLutMat = CoreUtils.CreateEngineMaterial(aerialPerspectiveLutShader);
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

        m_ScriptablePass.AtmosphereSettings = AtmosphereSettings;
        renderer.EnqueuePass(m_ScriptablePass);
    }

    protected override void Dispose(bool disposing)
    {
        m_ScriptablePass?.CleanupLut();
        CoreUtils.Destroy(m_ScriptablePass?.TransmittanceLutMat);
        CoreUtils.Destroy(m_ScriptablePass?.MultiScatteringLutMat);
        CoreUtils.Destroy(m_ScriptablePass?.SkyViewLutMat);
        CoreUtils.Destroy(m_ScriptablePass?.AerialPerspectiveLutMat);
        m_ScriptablePass = null;
    }
}