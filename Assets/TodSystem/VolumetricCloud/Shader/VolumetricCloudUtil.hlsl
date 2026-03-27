#ifndef VOLUMETRIC_CLOUD_UTIL_HLSL
#define VOLUMETRIC_CLOUD_UTIL_HLSL

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Assets/TodSystem/Atmosphere/Shader/AtmosphereParams.hlsl"

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Assets/TodSystem/Atmosphere/Shader/Transmittance.hlsl"

#include "VolumetricCloudParams.hlsl"

float InterleavedGradientNoise(float2 pixelPos)
{
    return frac(sin(dot(pixelPos.xy,
                       float2(12.9898,78.233)))
               * 43758.5453123);
}

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

float RayIntersectSphere(float3 center, float radius, float3 rayOrigin, float3 rayDirection)
{
    float3 originToCenter = rayOrigin - center;
    float halfB = dot(originToCenter, rayDirection);
    float c = dot(originToCenter, originToCenter) - radius * radius;
    float discriminant = halfB * halfB - c;

    if (discriminant < 0.0f)
    {
        return -1.0f;
    }

    float sqrtDiscriminant = sqrt(max(discriminant, 0.0f));
    float tNear = -halfB - sqrtDiscriminant;
    float tFar = -halfB + sqrtDiscriminant;
    float t = (tNear >= 0.0f) ? tNear : tFar;

    return (t >= 0.0f) ? t : -1.0f;
}

float Remap(float value, float OldMin, float OldMax, float NewMin, float NewMax)
{
    if (OldMax == OldMin)
    {
        return 0;
    }
    float temp = (NewMax - NewMin) * (value - OldMin) / (OldMax - OldMin);
    return saturate(temp + NewMin);
}

//camera to world ray
ViewRay CreateViewRayByScreenUV(float2 screenUV, float depth)
{
    ViewRay output;
    float3 ScenePositionWS = ComputeWorldSpacePosition(screenUV, depth, UNITY_MATRIX_I_VP);
    //全局体积云与相机位置无关
    float3 CameraPositionWS = GetCameraPositionWS();
    output.positionWS = CameraPositionWS;
    output.viewDirWS = normalize(ScenePositionWS - CameraPositionWS);
    return output;
}

CloudCoverage CalculateCloudCoverage(Texture2D cloudMap, SamplerState spl,float2 uv)
{
    float4 cloudMapValue = SAMPLE_TEXTURE2D_LOD(cloudMap, spl, uv,0);
    
    CloudCoverage output;
    output.coverage =cloudMapValue.r;
    output.typeIndex = cloudMapValue.g;
    return output;
}

CloudProperties CalculateCloudProperties(float3 uvw,float3 position, CloudCoverage cloudCoverage,
    Texture3D NoiseTex, SamplerState NoiseSampler, 
    Texture2D CloudLut, SamplerState CloudLutSampler)
{
    CloudProperties output;
    float4 Noise = SAMPLE_TEXTURE3D_LOD(NoiseTex, NoiseSampler, float3(position.xzy)  * _ShapeNoiseScale,0);
    float4 CloudLutValue = SAMPLE_TEXTURE2D(CloudLut, CloudLutSampler, float2(cloudCoverage.typeIndex, uvw.z));
    float shape = Remap(Noise.r, 1 -cloudCoverage.coverage,1,0,1);
    float fbmWorley = (0.625 * Noise.g + 0.375 * Noise.b) * _DetailNoiseScale;
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

    float2 uv = position.xz / float2(_CloudMapSize, _CloudMapSize) + float2(0.5f,0.5f);
    float distance = sqrt( uv.x*uv.x + uv.y*uv.y -uv.x - uv.y +0.5f);//distance to (0.5,0.5)
    if (distance > _WeatherMapScale * 0.5f)
    {
        CloudProperties res;
        return ZERO_INITIALIZE(CloudProperties,res);
    }
    // float4 cloudMapValue  = SAMPLE_TEXTURE2D(cloudMap,spl, uv);
    CloudCoverage coverage = CalculateCloudCoverage(cloudMap, couldMapSampler, uv);
    float distanceToCenter = length(position + float3(0, atmosphereParams.PlanetRadius, 0));
    float height01 = (distanceToCenter - atmosphereParams.PlanetRadius - cloudParams.CloudLayerLowHeight)
                   / (cloudParams.CloudLayerHighHeight - cloudParams.CloudLayerLowHeight);
    height01 = saturate(height01);

    float3 uvw = float3(position.xz,height01);
    CloudProperties properties = CalculateCloudProperties(uvw, position,coverage,NoiseTexture,noiseSampler,cloudLut,cloudLutSampler);
        
    return properties;
}


float4 Calculate(ViewRay viewRay, float2 uv,
    Texture2D cloudMap, SamplerState couldMapSampler, 
    Texture3D NoiseTexture, SamplerState noiseSampler,
    Texture2D cloudLut, SamplerState cloudLutSampler)
{
    AtmosphereParams atmosphereParams = GetAtmosphereParameter();
    CloudParams cloudParams = GetCloudParams();
    
    float HitEarth = RayIntersectSphere(float3(0,0,0), atmosphereParams.PlanetRadius,
        viewRay.positionWS + float3(0,atmosphereParams.PlanetRadius,0),viewRay.viewDirWS);
    bool isHitEarth = HitEarth > 0;
    if (isHitEarth)
    {
        return float4(0,0,0,1);
    }

    float lowHitLength = RayIntersectSphereLength(float3(0,0,0),
    atmosphereParams.PlanetRadius + cloudParams.CloudLayerLowHeight, 
    viewRay.positionWS + float3(0,atmosphereParams.PlanetRadius,0), 
    viewRay.viewDirWS);
    float highHitLength = RayIntersectSphereLength(float3(0,0,0),
        atmosphereParams.PlanetRadius + cloudParams.CloudLayerHighHeight, 
        viewRay.positionWS + float3(0,atmosphereParams.PlanetRadius,0), 
        viewRay.viewDirWS);
    if (lowHitLength < 0 && highHitLength < 0 )//视线和云层球壳不相交
    {
        return float4(0,0,0,1);
    }
    if (lowHitLength > highHitLength && highHitLength > 0)//从太空向地面看
    {
        float temp = lowHitLength;
        lowHitLength = highHitLength;
        highHitLength = temp;
    }
    lowHitLength = max(lowHitLength, 0.0f);
    if (highHitLength <= lowHitLength)
    {
        return float4(0,0,0,1);
    }

    int RayMarchingSteps = min(_RayMarchingSteps,MAX_RAYMARCHING_STEP);

    float stepLength = (highHitLength -lowHitLength)/RayMarchingSteps;
    float3 stepPosition = viewRay.positionWS + (lowHitLength)* viewRay.viewDirWS + viewRay.viewDirWS * stepLength *  InterleavedGradientNoise(uv);
    
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
        
        float3 sigma_s = (properties.density * sigma_t).xxx ;
        float stepTransmittance = exp(-properties.density * sigma_t * stepLength);


        float3 lightDir = GetMainLight().direction;//Sun
        
        float3 sunTransmittance =  TransmittanceToAtmosphereByLut(atmosphereParams, stepPosition + float3(0,atmosphereParams.PlanetRadius,0), lightDir, _transmittanceLut, sampler_transmittanceLut);

        

        float sunVisibility =1;
        {

            int SunStepTimes = 4;

            float SunRayMarchingTotalLength = RayIntersectSphereLength(float3(0,0,0),atmosphereParams.PlanetRadius + cloudParams.CloudLayerHighHeight,
                 stepPosition + float3(0,atmosphereParams.PlanetRadius,0), lightDir );
            if (SunRayMarchingTotalLength <= 0.0f)
            {
                SunRayMarchingTotalLength = 0.0f;
            }
            float SunRayStepLength = SunRayMarchingTotalLength / SunStepTimes;

            float3 SunRayMarchingPositionWS = stepPosition + lightDir * SunRayStepLength * 0.5;

            for (uint j = 0; j < SunStepTimes; ++j)
            {

                float stepDestiny = CalculateCloudPropertiesByPosition(SunRayMarchingPositionWS,
                cloudMap,  couldMapSampler, 
                NoiseTexture, noiseSampler,
                cloudLut, cloudLutSampler ).density;

                sunVisibility *= exp(-stepDestiny  * sigma_t * min(SunRayStepLength,MAX_SUN_RAY_MARCHING_LENGTH));

                SunRayMarchingPositionWS += lightDir * SunRayStepLength;
                //I don't know how long should the ray step ,so multi 2 .

            }
        }
        

        float3 stepScattering = GetMainLight().color 
                                * sunTransmittance 
                                * dualLobPhase(0.8, -0.5, 0.2, dot(-viewRay.viewDirWS, lightDir))
                                * sunVisibility;//TODO:
        
        scattering += stepScattering * transmittance * sigma_s * stepLength;
        
        transmittance *= stepTransmittance;
        
        stepPosition += stepLength * viewRay.viewDirWS;
    }
    return float4(scattering,transmittance);
}

#endif
