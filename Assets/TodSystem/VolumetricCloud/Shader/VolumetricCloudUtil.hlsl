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

CloudProperties CalculateCloudProperties(float3 uvw, CloudCoverage cloudCoverage,
    Texture3D NoiseTex, SamplerState NoiseSampler, 
    Texture2D CloudLut, SamplerState CloudLutSampler)
{
    CloudProperties output;
    float4 Noise = SAMPLE_TEXTURE3D_LOD(NoiseTex, NoiseSampler, uvw * _ShapeNoiseScale,0);
    float4 CloudLutValue = SAMPLE_TEXTURE2D(CloudLut, CloudLutSampler, float2(cloudCoverage.typeIndex, uvw.z));
    float heightMultipler = CloudLutValue.r;//记录着不同index的云在高度上的分布；
    float shape = Remap(Noise.r , 1 -cloudCoverage.coverage * heightMultipler,1.0f,0.0f,1.0f);
    float fbmWorley = (0.625 * Noise.g + 0.375 * Noise.b ) ;
    //output.density = saturate(shape - fbmWorley * _ErosionStrength);
    // output.density = Remap(shape, 1-fbmWorley,1,0,1);
    // output.density = Remap(output.density, 1-fbmWorley, 1,0,1);
    output.density = Remap(shape,fbmWorley * _ErosionStrength, 1,0,1);
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

    float3 uvw = float3(uv,height01);
    CloudProperties properties = CalculateCloudProperties(uvw, coverage,NoiseTexture,noiseSampler,cloudLut,cloudLutSampler);
        
    return properties;
}


struct Result
{
    float4 ScatteringTransmittance;
    float3 meanPosition;
};

Result Calculate(ViewRay viewRay, float2 uv,
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
        Result res;
        res.ScatteringTransmittance = float4(0,0,0,1);
        res.meanPosition = float3(0,0,0);
        return res;
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
        Result res;
        res.ScatteringTransmittance = float4(0,0,0,1);
        res.meanPosition = float3(0,0,0);
        return res;
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
        Result res;
        res.ScatteringTransmittance = float4(0,0,0,1);
        res.meanPosition = float3(0,0,0);
        return res;
    }

    int RayMarchingSteps = min(_RayMarchingSteps,MAX_RAYMARCHING_STEP);

    float stepLength = (highHitLength -lowHitLength)/RayMarchingSteps;
    float3 stepPosition = viewRay.positionWS + (lowHitLength)* viewRay.viewDirWS + viewRay.viewDirWS * stepLength *  InterleavedGradientNoise(uv);
    
    float sigma_t = lerp(NORMAL_CLOUD_SIGMA_EXTINCTION,
                    RAINY_CLOUD_SIGMA_EXTINCTION, 
                    _isRainyCloud);
    float albedo = 0.95;
    float3 lightDir = GetMainLight().direction;//Sun
    float3 lightColor = GetMainLight().color;
    float transmittance = 1;
    float3 scattering = 0;
    
    float3 rayHitPos = float3(0,0,0);
    float rayHitPosWeight = 0;
    
    for (int i = 0; i < RayMarchingSteps ; ++i)
    {
        CloudProperties properties = 
            CalculateCloudPropertiesByPosition(stepPosition,
                cloudMap,  couldMapSampler, 
                NoiseTexture, noiseSampler,
                cloudLut, cloudLutSampler );
        
        float3 sigma_s = (properties.density * sigma_t * albedo).xxx ;
        float stepTransmittance = exp(-properties.density * sigma_t * stepLength);
        
        float3 sunTransmittance =  TransmittanceToAtmosphereByLut(atmosphereParams, stepPosition + float3(0,atmosphereParams.PlanetRadius,0), lightDir, _transmittanceLut, sampler_transmittanceLut);
        
        //向太阳采样
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

            }
        }
        

        float3 stepScattering = GetMainLight().color 
                                * sunTransmittance 
                                * dualLobPhase(0.8, -0.1, 0.45, dot(-viewRay.viewDirWS, lightDir))
                                * sunVisibility;//TODO:
        //add some hack
        float3 ambientColor = _CloudAmbientColor * lerp(0.35,1.0,properties.ao);
        
        float powder = 1.0 - exp(-properties.density * stepLength * _PowderSterngth);
        
        float3 stepLighting  = stepScattering + ambientColor + powder * lightColor * sunVisibility ;
        
        scattering += stepLighting * transmittance * sigma_s * stepLength;
        transmittance *= stepTransmittance;

        
        rayHitPos += stepPosition * transmittance;
        rayHitPosWeight += transmittance;
        
        stepPosition += stepLength * viewRay.viewDirWS;
        if (transmittance <0.03)
        {
            break;
        }
    }
    Result res;
    res.ScatteringTransmittance = float4(scattering,transmittance);
    res.meanPosition = rayHitPos/rayHitPosWeight;
    return res;
}

#endif
