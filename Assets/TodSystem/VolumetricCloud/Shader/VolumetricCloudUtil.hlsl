#ifndef VOLUMETRIC_CLOUD_UTIL_HLSL
#define VOLUMETRIC_CLOUD_UTIL_HLSL

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Assets/TodSystem/Atmosphere/Shader/Helper.hlsl"
#include "Assets/TodSystem/Atmosphere/Shader/AtmosphereParams.hlsl"
#include "VolumetricCloudParams.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Assets/TodSystem/Atmosphere/Shader/Transmittance.hlsl"

struct ViewRay
{
    float3 positionWS;
    float3 viewDirWS;
};

struct CloudCoverage
{
    float coverage;
    float typeIndex;
};

struct CloudProperties
{
    float density;
    float ao;
};

float Remap(float value, float OldMin, float OldMax, float NewMin, float NewMax)
{
    float temp = (NewMax - NewMin) * (value - OldMin) / (OldMax - OldMin);
    return saturate(temp + NewMin);
}

//camera to world ray
ViewRay CreateViewRayByScreenUV ( float2 screenUV, float depth)
{
    ViewRay output;
    float3 ScenePositionWS =  ComputeWorldSpacePosition(screenUV, depth, UNITY_MATRIX_I_VP);
    //全局体积云与相机位置无关
    float3 CameraPositionWS = GetCameraPositionWS();
    output.positionWS = CameraPositionWS;
    output.viewDirWS = normalize(ScenePositionWS - CameraPositionWS);
    return output;
}

CloudCoverage CalculateCloudCoverage(Texture2D cloudMap, SamplerState spl,float2 uv)
{
    float4 cloudMapValue = SAMPLE_TEXTURE2D(cloudMap, spl, uv);
    
    CloudCoverage output;
    output.coverage =cloudMapValue.r;
    output.typeIndex = cloudMapValue.g;
    return output;
}

CloudProperties CalculateCloudProperties(float3 uvw, CloudCoverage cloudCoverage, 
    Texture3D NoiseTex, SamplerState NoiseSampler, 
    Texture2D CloudLut, SamplerState CloudLutSampler)
{
    CloudProperties output;
    float4 Noise = SAMPLE_TEXTURE3D(NoiseTex, NoiseSampler, uvw * 10 * _ShapeNoiseScale);
    float4 CloudLutValue = SAMPLE_TEXTURE2D(CloudLut, CloudLutSampler, float2(cloudCoverage.typeIndex, uvw.z));
    float shape = Remap(Noise.r, 1 -cloudCoverage.coverage,1,0,1);
    float fbmWorley = 0.625 * Noise.g + 0.375 * Noise.b;
    output.density = saturate(shape - fbmWorley * _ErosionStrength);
    output.ao = CloudLutValue.g;
    
    return output;
}


CloudProperties CalculateCloudPropertiesByPosition(float3 position,
    Texture2D cloudMap, SamplerState couldMapSampler, 
    Texture3D NoiseTexture, SamplerState noiseSampler,
    Texture2D cloudLut, SamplerState cloudLutSampler)
{
    AtmosphereParams atmosphereParams = GetAtmosphereParameter();
    CloudParams cloudParams = GetCloudParams();
    
    // CloudProperties stepProperties = CalculateCloudPropertiesByPositionWS(stepPosition);
    float2 uv = ((position.xz *(1+ _WeatherMapScale))/float2(40000,40000));
    // float4 cloudMapValue  = SAMPLE_TEXTURE2D(cloudMap,spl, uv);
    CloudCoverage coverage = CalculateCloudCoverage(cloudMap, couldMapSampler, uv);
    float distanceToCenter = length(position + float3(0, atmosphereParams.PlanetRadius, 0));
    float height01 = (distanceToCenter - atmosphereParams.PlanetRadius - cloudParams.CloudLayerLowHeight) 
           / (cloudParams.CloudLayerHighHeight - cloudParams.CloudLayerLowHeight);
    height01 = saturate(height01);

    float3 uvw = float3(uv.x,height01,uv.y);
    CloudProperties properties = CalculateCloudProperties(uvw,coverage,NoiseTexture,noiseSampler,cloudLut,cloudLutSampler);
        
    return properties;
}


float4 Calculate(ViewRay viewRay, 
    Texture2D cloudMap, SamplerState couldMapSampler, 
    Texture3D NoiseTexture, SamplerState noiseSampler,
    Texture2D cloudLut, SamplerState cloudLutSampler)
{
    AtmosphereParams atmosphereParams = GetAtmosphereParameter();
    CloudParams cloudParams = GetCloudParams();
    
    float lowHitLength = RayIntersectSphereLength(float3(0,0,0),
    atmosphereParams.PlanetRadius + cloudParams.CloudLayerLowHeight, 
    viewRay.positionWS + float3(0,atmosphereParams.PlanetRadius,0), 
    viewRay.viewDirWS);
    float highHitLength = RayIntersectSphereLength(float3(0,0,0),
        atmosphereParams.PlanetRadius + cloudParams.CloudLayerHighHeight, 
        viewRay.positionWS + float3(0,atmosphereParams.PlanetRadius,0), 
        viewRay.viewDirWS);
    if (lowHitLength < 0 || highHitLength < 0) return float4(0, 0, 0, 1);
    if (highHitLength <= lowHitLength) return float4(0, 0, 0, 1);
    int RayMarchingSteps = min(_RayMarchingSteps,MAX_RAYMARCHING_STEP);

    float stepLength = (highHitLength -lowHitLength)/RayMarchingSteps;
    float3 stepPosition = viewRay.positionWS + (highHitLength -0.5 * stepLength)* viewRay.viewDirWS; 
    
    float sigma_t = lerp(NORMAL_CLOUD_SIGMA_EXTINCTION,
                    RAINY_CLOUD_SIGMA_EXTINCTION, 
                    _isRainyCloud);
    
    float transmittance = 1;
    float3 scattering = 0;
    
    for (int i = 0; i < RayMarchingSteps ; ++i)
    {
        CloudProperties properties = 
            CalculateCloudPropertiesByPosition(stepPosition,
                cloudMap,  couldMapSampler, 
                NoiseTexture, noiseSampler,
                cloudLut, cloudLutSampler );
        
        float stepTransmittance = exp(-properties.density *sigma_t * stepLength);
        float3 sigma_s = properties.density  ;

        float3 lightDir = GetMainLight().direction;//Sun
        
        float3 sunTransmittance =  TransmittanceToAtmosphereByLut(atmosphereParams, stepPosition + float3(0,atmosphereParams.PlanetRadius,0), lightDir, _TransmittanceLut, sampler_TransmittanceLut);

        

        float sunVisibility = properties.density;
        {
            float3 SunRayMarchingPositionWS = stepPosition;
            float SunStepTimes = 2;
             
            for (uint j = 0; j < SunStepTimes; ++j)
            {
                SunRayMarchingPositionWS += GetMainLight().direction * 2 * stepLength;
                float stepDestiny = CalculateCloudPropertiesByPosition(SunRayMarchingPositionWS,
                cloudMap,  couldMapSampler, 
                NoiseTexture, noiseSampler,
                cloudLut, cloudLutSampler ).density;
                sunVisibility *= exp(-stepDestiny * 2 * sigma_t * stepLength);
                //I don't know how long should the ray step ,so multi 2 .

            }
        }
        
        float3 stepScattering = GetMainLight().color 
                                * sunTransmittance 
                                * dualLobPhase(0.5, -0.5, 0.2, dot(-viewRay.viewDirWS, lightDir)) 
                                * sunVisibility;//TODO:
        
        scattering += stepScattering * transmittance * sigma_s * stepLength;
        
        transmittance *= stepTransmittance;
        
        stepPosition -= stepLength * viewRay.viewDirWS;
    }
    return float4(scattering,transmittance);
}

#endif
