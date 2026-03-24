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
    
    public FloatParameter CloudLayerLowHeight  = new FloatParameter(4000, true);
    public FloatParameter CloudLayerHighHeight = new FloatParameter(7000, true);
    public ClampedIntParameter RayMarchingSteps = new(32,1,128,false);
    public ClampedFloatParameter ErosionStrength = new(0.5f, 0, 1, false);
    public ClampedFloatParameter IsRainyCloud = new ClampedFloatParameter(0.01f, 0, 1, false);
    public  ClampedFloatParameter  WeatherMapScale  = new (0.1f, 0.0f, 1.0f, false);
    public  ClampedFloatParameter ShapeNoiseScale   = new (0.1f, 0.0f, 1.0f, false);
    public  ClampedFloatParameter DetailNoiseScale  = new (0.1f, 0.0f, 1.0f, false);
}
