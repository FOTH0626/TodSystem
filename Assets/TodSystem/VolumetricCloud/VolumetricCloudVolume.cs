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
    public ClampedIntParameter RayMarchingSteps = new(32,1,64,false);
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
    [InspectorName("顶部偏移方向(XZ)")]
    public Vector2Parameter TopOffsetDirection = new(new Vector2(1.0f, 0.35f), false);
    [InspectorName("顶部偏移距离（米）")]
    public ClampedFloatParameter TopOffsetDistance = new(1800.0f, 0.0f, 8000.0f, false);
    [InspectorName("顶部塑形开始高度")]
    public ClampedFloatParameter TopShapeBlendStart = new(0.35f, 0.0f, 1.0f, false);
    [InspectorName("顶部塑形结束高度")]
    public ClampedFloatParameter TopShapeBlendEnd = new(0.88f, 0.0f, 1.0f, false);
    [InspectorName("启用时空累积")]
    public BoolParameter EnableTemporalAccumulation = new(true, BoolParameter.DisplayType.EnumPopup, false);
    [InspectorName("历史权重")]
    public ClampedFloatParameter TemporalHistoryWeight = new(0.85f, 0.0f, 0.98f, false);
    [InspectorName("世界位置拒绝阈值（米）")]
    public ClampedFloatParameter TemporalWorldPosThreshold = new(300.0f, 25.0f, 1000.0f, false);
    [InspectorName("云量拒绝阈值")]
    public ClampedFloatParameter TemporalOpacityThreshold = new(0.15f, 0.01f, 1.0f, false);
    [InspectorName("亮度拒绝阈值")]
    public ClampedFloatParameter TemporalLuminanceThreshold = new(0.75f, 0.05f, 4.0f, false);
    [InspectorName("邻域夹取强度")]
    public ClampedFloatParameter TemporalClampStrength = new(1.0f, 0.0f, 1.0f, false);

    [HideInInspector]
    public BoolParameter EnableJointBilateralDenoise = new(true, BoolParameter.DisplayType.EnumPopup, false);
    [HideInInspector]
    public ClampedIntParameter JointBilateralRadius = new(2, 1, 3, false);
    [HideInInspector]
    public ClampedFloatParameter JointBilateralSpatialSigma = new(1.5f, 0.5f, 4.0f, false);
    [HideInInspector]
    public ClampedFloatParameter JointBilateralDepthSigma = new(0.02f, 0.001f, 0.25f, false);
    [HideInInspector]
    public ClampedFloatParameter JointBilateralOpacitySigma = new(0.12f, 0.01f, 1.0f, false);
    [HideInInspector]
    public ClampedFloatParameter JointBilateralLuminanceSigma = new(0.35f, 0.01f, 4.0f, false);
}
