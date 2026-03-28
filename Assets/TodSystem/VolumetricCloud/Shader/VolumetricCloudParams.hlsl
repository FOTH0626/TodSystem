#ifndef VOLUMETRIC_CLOUD_PARAMS_HLSL
#define VOLUMETRIC_CLOUD_PARAMS_HLSL

#define MAX_RAYMARCHING_STEP 32
#define NORMAL_CLOUD_SIGMA_EXTINCTION 0.06
#define RAINY_CLOUD_SIGMA_EXTINCTION 0.12
#define MAX_SUN_RAY_MARCHING_LENGTH 1000

CBUFFER_START(UnityPerMaterial)
           
float _CloudLayerLowHeight;
float _CloudLayerHighHeight;     
float _CloudMapSize;
int _RayMarchingSteps;
float _ErosionStrength;
float _isRainyCloud;

float _WeatherMapScale;
float _ShapeNoiseScale;
float _DetailNoiseScale;

float _AmbientStrength;
float _SkyAmbientStrength;
float _MultiScatteringStrength;
float _PowderSterngth;
float4 _CloudAmbientColor;

CBUFFER_END


struct CloudParams
{
   float CloudLayerLowHeight;
   float CloudLayerHighHeight;   
};

CloudParams GetCloudParams()
{
   CloudParams output;
   output.CloudLayerLowHeight = _CloudLayerLowHeight;
   output.CloudLayerHighHeight = _CloudLayerHighHeight;
   return output;
}

#endif
