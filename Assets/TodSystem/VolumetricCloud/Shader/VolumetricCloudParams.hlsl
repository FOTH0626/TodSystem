#ifndef VOLUMETRIC_CLOUD_PARAMS_HLSL
#define VOLUMETRIC_CLOUD_PARAMS_HLSL

#define MAX_RAYMARCHING_STEP 32
#define NORMAL_CLOUD_SIGMA_EXTINCTION 0.06
#define RAINY_CLOUD_SIGMA_EXTINCTION 0.12


CBUFFER_START(UnityPerMaterial)
           
float _CloudLayerLowHeight;
float _CloudLayerHighHeight;     
int _RayMarchingSteps;
float _ErosionStrength;
float _isRainyCloud;

float _WeatherMapScale;
float _ShapeNoiseScale;
float _DetailNoiseScale;

CBUFFER_END

TEXTURE2D(_TransmittanceLut);
SAMPLER(sampler_TransmittanceLut);

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
