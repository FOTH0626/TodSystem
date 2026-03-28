using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

[Serializable, VolumeComponentMenu("TOD/VolumetricCloud")]
public class VolumetricCloudVolume :   VolumeComponent , IPostProcessComponent
{
    public BoolParameter state = new(false, BoolParameter.DisplayType.EnumPopup, overrideState: true);

    public bool IsActive() => state.value;
    public bool IsTileCompatible() => false;
    [InspectorName("云层底部高度")]
    public FloatParameter CloudLayerLowHeight  = new FloatParameter(4000, true);
    [InspectorName("云层顶部高度")]
    public FloatParameter CloudLayerHighHeight = new FloatParameter(7000, true);
    [InspectorName("云图覆盖范围（米）")]
    public ClampedFloatParameter CloudMapSize = new(16000, 4000, 32000,false);
    [InspectorName("步进次数")]
    public ClampedIntParameter RayMarchingSteps = new(32,1,32,false);
    [InspectorName("侵蚀强度")]
    public ClampedFloatParameter ErosionStrength = new(0.5f, 0, 1, false);
    // [HideInInspector]
    public ClampedFloatParameter IsRainyCloud = new ClampedFloatParameter(0.01f, 0, 1, false);
    [InspectorName("天气覆盖倍率（相比云图大小）")]
    public  ClampedFloatParameter  WeatherMapScale  = new (1f, 1f, 10f, false);
    [InspectorName("噪声强度")]
    public  ClampedFloatParameter ShapeNoiseScale   = new (0.1f, 0.01f, 1.0f, false);
    [InspectorName("细节强度"),HideInInspector]
    public  ClampedFloatParameter DetailNoiseScale  = new (0.1f, 0.0f, 1.0f, false);
    [InspectorName("环境光强度")]
    public ClampedFloatParameter AmbientStrength = new(0.35f, 0.0f, 4.0f, false);
    [InspectorName("天空环境光强度")]
    public ClampedFloatParameter SkyAmbientStrength = new(0.6f, 0.0f, 4.0f, false);
    [InspectorName("多重散射强度")]
    public ClampedFloatParameter MultiScatteringStrength = new(1.15f, 0.0f, 4.0f, false);
    [InspectorName("Powder 强度")]
    public ClampedFloatParameter PowderStrength = new(2.0f, 0.0f, 8.0f, false);
}
