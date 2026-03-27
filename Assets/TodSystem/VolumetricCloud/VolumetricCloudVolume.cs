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
    public FloatParameter CloudLayerHighHeight = new FloatParameter(7000, true);
    public ClampedFloatParameter CloudMapSize = new(16000, 4000, 32000,false);
    public ClampedIntParameter RayMarchingSteps = new(32,1,32,false);
    public ClampedFloatParameter ErosionStrength = new(0.5f, 0, 1, false);
    [HideInInspector]
    public ClampedFloatParameter IsRainyCloud = new ClampedFloatParameter(0.01f, 0, 1, false);
    public  ClampedFloatParameter  WeatherMapScale  = new (1f, 1f, 10f, false);
    public  ClampedFloatParameter ShapeNoiseScale   = new (0.1f, 0.0001f, 1.0f, false);
    public  ClampedFloatParameter DetailNoiseScale  = new (0.1f, 0.0f, 1.0f, false);
}
