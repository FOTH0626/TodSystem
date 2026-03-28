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

struct CloudFootprint
{
    float2 baseUv;
    float2 topUv;
    float2 shapeUv;
    float topBlend;
    float baseMask;
    float topMask;
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
    //噪声生成工具在Script/Helper/ 目录下
    //R通道是SimpleX-Worley联合噪声（用于基础形状），GB通道是fbm Worley噪声（用于雕刻）, A通道是Curl噪声（用于模拟湍流）。
    float4 Noise = SAMPLE_TEXTURE3D_LOD(NoiseTex, NoiseSampler, uvw * _ShapeNoiseScale,0);
    float4 CloudLutValue = SAMPLE_TEXTURE2D(CloudLut, CloudLutSampler, float2(cloudCoverage.typeIndex, uvw.z));
    float heightMultipler = CloudLutValue.r;//记录着不同index的云在高度上的分布；
    float shape = Remap(Noise.r , 1 -cloudCoverage.coverage * heightMultipler,1.0f,0.0f,1.0f);
    float fbmWorley = (0.625 * Noise.g + 0.375 * Noise.b ) ;
    output.density = saturate(shape - fbmWorley * _ErosionStrength);
    // output.density = Remap(shape, 1-fbmWorley,1,0,1);
    // output.density = Remap(output.density, 1-fbmWorley, 1,0,1);
    // output.density = Remap(shape,fbmWorley * _ErosionStrength, 1,0,1);
    output.ao = CloudLutValue.g;

    
    return output;
}

float GetWeatherMapMask(float2 uv)
{
    float distance = sqrt(uv.x * uv.x + uv.y * uv.y - uv.x - uv.y + 0.5f);
    return distance <= _WeatherMapScale * 0.5f ? 1.0f : 0.0f;
}

// 数学：
//   对同一世界点构造两套 weather footprint：底部 uv_b 和顶部 uv_t = uv_b + d，
//   再用 topBlend = smoothstep(h_start, h_end, height01) 在两者之间插值得到 shapeUv。
// 物理：
//   真实积云顶部常受浮升和风切变影响，相比云底更容易外扩、偏移或形成 overhang，
//   所以不同高度不应共享完全相同的水平截面。
// 原理：
//   底部 footprint 继续负责天气分布的锚点，顶部 footprint 提供第二个宏观截面；
//   ray-march 时再按高度在两者之间过渡，把“二维底图挤出”改成“上下两层轮廓混合”。
// 编写原因：
//   之前的塑形完全依赖单一 XZ footprint，云柱上下严格对齐，体块会显得像 2D 云图加厚。
//   这个函数用很低的代价把顶部偏移和外扩引入密度场，让轮廓更容易出现蘑菇头、卷边和悬挑。
CloudFootprint CalculateCloudFootprint(float3 position, float height01, CloudParams cloudParams)
{
    CloudFootprint output;

    output.baseUv = position.xz / float2(_CloudMapSize, _CloudMapSize) + float2(0.5f, 0.5f);

    float2 topOffsetDirection = cloudParams.TopOffsetDirection;
    float directionLength = length(topOffsetDirection);
    if (directionLength < 1e-4f)
    {
        topOffsetDirection = float2(1.0f, 0.0f);
    }
    else
    {
        topOffsetDirection /= directionLength;
    }

    float2 topOffsetUv = topOffsetDirection * (cloudParams.TopOffsetDistance / max(_CloudMapSize, 1.0f));
    output.topUv = output.baseUv + topOffsetUv;

    float blendStart = min(cloudParams.TopShapeBlendStart, cloudParams.TopShapeBlendEnd - 1e-4f);
    float blendEnd = max(cloudParams.TopShapeBlendEnd, cloudParams.TopShapeBlendStart + 1e-4f);
    output.topBlend = smoothstep(blendStart, blendEnd, height01);
    output.shapeUv = lerp(output.baseUv, output.topUv, output.topBlend);

    output.baseMask = GetWeatherMapMask(output.baseUv);
    output.topMask = GetWeatherMapMask(output.topUv);
    return output;
}


CloudProperties CalculateCloudPropertiesByPosition(float3 position,
    Texture2D cloudMap, SamplerState couldMapSampler, 
    Texture3D NoiseTexture, SamplerState noiseSampler,
    Texture2D cloudLut, SamplerState cloudLutSampler)
{
    AtmosphereParams atmosphereParams = GetAtmosphereParameter();
    CloudParams cloudParams = GetCloudParams();

    float distanceToCenter = length(position + float3(0, atmosphereParams.PlanetRadius, 0));
    float height01 = (distanceToCenter - atmosphereParams.PlanetRadius - cloudParams.CloudLayerLowHeight)
                   / (cloudParams.CloudLayerHighHeight - cloudParams.CloudLayerLowHeight);
    height01 = saturate(height01);

    CloudFootprint footprint = CalculateCloudFootprint(position, height01, cloudParams);
    if (footprint.baseMask <= 0.0f && footprint.topMask <= 0.0f)
    {
        CloudProperties res;
        return ZERO_INITIALIZE(CloudProperties, res);
    }

    CloudCoverage bottomCoverage = CalculateCloudCoverage(cloudMap, couldMapSampler, footprint.baseUv);
    CloudCoverage topCoverage = CalculateCloudCoverage(cloudMap, couldMapSampler, footprint.topUv);
    bottomCoverage.coverage *= footprint.baseMask;
    topCoverage.coverage *= footprint.topMask;

    CloudCoverage coverage;
    coverage.coverage = lerp(bottomCoverage.coverage, topCoverage.coverage, footprint.topBlend);
    coverage.typeIndex = bottomCoverage.typeIndex;

    float3 uvw = float3(footprint.shapeUv, height01);
    CloudProperties properties = CalculateCloudProperties(uvw, coverage,NoiseTexture,noiseSampler,cloudLut,cloudLutSampler);
        
    return properties;
}


struct Result
{
    float4 ScatteringTransmittance;
    float3 meanPosition;
};

// 数学：
//   把单位方向向量映射到 sky-view LUT 的 2D 参数域，然后做一次 0 mip 采样。
//   这里等价于查询函数 L_sky(omega)，其中 omega 是球面方向。
// 物理：
//   sky-view LUT 代表大气对某个观察方向的出射辐亮度，可以把它看成云层收到的天空入射光的缓存。
// 原理：
//   体积云步进里会频繁查询天空光，单独封装这个函数可以保证所有采样都走同一套方向归一化和 UV 变换。
// 编写原因：
//   后面的环境光近似需要重复采样天空不同方向，如果每次都内联 UV 变换，既难读也容易在坐标约定上出错。
float3 SampleSkyLightingLut(float3 viewDir)
{
    return SAMPLE_TEXTURE2D_LOD(_skyViewLut, sampler_LinearClamp, ViewDirToUV(normalize(viewDir)), 0).rgb;
}

// 数学：
//   严格的天空环境光应该是半球积分 integral_hemisphere L_sky(omega) * phase_or_visibility d omega。
//   这里用 5 个代表方向和固定权重做离散求积，近似这个积分：
//   1 个天顶方向 + 2 个沿太阳切线的地平线方向 + 2 个侧向方向。
// 物理：
//   云不会只被太阳直射照亮，天空穹顶本身就是一个大面积蓝色光源。
//   背光面发黑通常不是“太阳不够亮”，而是缺少来自天空半球的二次入射能量。
// 原理：
//   选择太阳侧、背太阳侧和两个侧向，是为了保留天空辐亮度最重要的角向变化：
//   天顶更冷更亮，朝向太阳的地平线更暖更强，背光侧和横向再补足整体包裹感。
// 编写原因：
//   每个 ray-march step 都做真实半球积分代价太高，这个函数用很低成本补上“天空在照亮云”这件事，
//   直接改善背光区和云底过黑的问题，同时保留一定的方向性而不是简单加常量。
float3 ApproximateCloudSkyAmbient(float3 lightDir)
{
    float3 up = float3(0, 1, 0);
    float3 sunTangent = lightDir - up * dot(lightDir, up);
    if (dot(sunTangent, sunTangent) < 1e-4)
    {
        sunTangent = float3(1, 0, 0);
    }
    else
    {
        sunTangent = normalize(sunTangent);
    }

    float3 bitangent = normalize(cross(up, sunTangent));

    float3 zenith = SampleSkyLightingLut(up);
    float3 towardSunHorizon = SampleSkyLightingLut(normalize(up * 0.35 + sunTangent * 0.65));
    float3 awayFromSunHorizon = SampleSkyLightingLut(normalize(up * 0.35 - sunTangent * 0.65));
    float3 sideA = SampleSkyLightingLut(normalize(up * 0.45 + bitangent * 0.55));
    float3 sideB = SampleSkyLightingLut(normalize(up * 0.45 - bitangent * 0.55));

    return zenith * 0.45 +
           towardSunHorizon * 0.20 +
           awayFromSunHorizon * 0.20 +
           sideA * 0.075 +
           sideB * 0.075;
}

// 数学：
//   先由 T = exp(-tau) 反推光学厚度 tau = -ln(T)，再用 3 个衰减 octave 累加近似多次散射：
//   sum_i weight_i * exp(-tau * attenuation_i) * phase_i(cosTheta)。
//   attenuation 逐级降低，phase 的各向异性也逐级减弱，模拟“散射次数越多，方向性越弱”的趋势。
// 物理：
//   云内部的多重散射会把太阳能量在介质里反复转向，结果是亮能量渗进阴影区，
//   同时前向强峰被逐步抹平，云体内部看起来更柔、更厚、更不容易死黑。
// 原理：
//   真正的多重散射需要做高维体积分或预计算体 LUT，这对当前的逐像素步进云来说太贵。
//   这里借用 Frostbite/HZD 一类体积云常见的 octave 近似思想，用“可见度衰减 + 相位函数逐级变钝”
//   的办法，压缩出一个稳定、便宜、可调的多重散射项。
// 编写原因：
//   现有实现只有单次散射和 powder，高密度区域在背光面会迅速掉到很黑。
//   这个函数的职责就是在不引入二次光线步进的前提下，把缺失的内部回光补回来。
float3 EvaluateCloudMultipleScattering(float3 sunLuminance, float sunVisibility, float cosTheta)
{
    const int octaveCount = 3;
    float opticalDepth = -log(max(sunVisibility, 1e-3));

    float attenuation = 0.65;
    float contribution = 0.60;
    float forwardEccentricity = 0.55;
    float backwardEccentricity = -0.08;
    float3 lighting = 0;

    [unroll]
    for (int octave = 0; octave < octaveCount; ++octave)
    {
        float octaveVisibility = exp(-opticalDepth * attenuation);
        float phase = dualLobPhase(forwardEccentricity, backwardEccentricity, 0.30, cosTheta);
        lighting += contribution * octaveVisibility * phase;

        attenuation *= 0.55;
        contribution *= 0.70;
        forwardEccentricity *= 0.75;
        backwardEccentricity *= 0.50;
    }

    return sunLuminance * lighting;
}

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
    
    [loop]
    for (int i = 0; i < RayMarchingSteps ; ++i)
    {
        CloudProperties properties = 
            CalculateCloudPropertiesByPosition(stepPosition,
                cloudMap,  couldMapSampler, 
                NoiseTexture, noiseSampler,
                cloudLut, cloudLutSampler );
        
        float3 sigma_s = (properties.density * sigma_t * albedo).xxx ;
        float stepTransmittance = exp(-properties.density * sigma_t * stepLength);
        float3 atmospherePosition = stepPosition + float3(0,atmosphereParams.PlanetRadius,0);
        float3 sunTransmittance =  TransmittanceToAtmosphereByLut(atmosphereParams, atmospherePosition, lightDir, _transmittanceLut, sampler_transmittanceLut);
        
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


            [loop]
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
        
        float cosTheta = dot(-viewRay.viewDirWS, lightDir);
        float3 sunLuminance = lightColor * sunTransmittance;

        float3 stepScattering = sunLuminance
                                * dualLobPhase(0.8, -0.1, 0.45, cosTheta)
                                * sunVisibility;
        float3 multiScattering = EvaluateCloudMultipleScattering(sunLuminance, sunVisibility, cosTheta)
                                 * _MultiScatteringStrength;
        float3 ambientColor = (_CloudAmbientColor.rgb * _AmbientStrength +
                               ApproximateCloudSkyAmbient(lightDir) * _SkyAmbientStrength)
                              * lerp(0.35,1.0,properties.ao);
        
        float powder = 1.0 - exp(-properties.density * stepLength * _PowderSterngth);
        
        float3 stepLighting  = stepScattering + multiScattering + ambientColor + powder * sunLuminance * sunVisibility ;
        
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
