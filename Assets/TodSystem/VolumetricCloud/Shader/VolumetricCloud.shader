Shader "TOD/VolumetricCloud/VolumetricCloud"
{
    Properties
    {
        [NoScaleOffset]_CloudMap("Cloud Map", 2D) = "white" {}
        [NoScaleOffset]_NoiseTexture("3D Noise", 3D) = "defaulttexture" {}
        [NoScaleOffset]_CloudLut("Cloud Lut", 2D) = "white" {}
    }

    HLSLINCLUDE

    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/GlobalSamplers.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

    TEXTURE2D(_CloudMap);
    SAMPLER(sampler_CloudMap);
    TEXTURE3D(_NoiseTexture);
    SAMPLER(sampler_NoiseTexture);
    TEXTURE2D(_CloudLut);
    SAMPLER(sampler_CloudLut);
    TEXTURE2D(_transmittanceLut);
    SAMPLER(sampler_transmittanceLut);
    TEXTURE2D(_aerialPerspectiveLut);
    SAMPLER(sampler_aerialPerspectiveLut);
    TEXTURE2D(_multiScatteringLut);
    TEXTURE2D(_skyViewLut);

    TEXTURE2D(_CloudBlueNoiseTexture);
    TEXTURE2D_X(_CloudCurrentColorTexture);
    TEXTURE2D_X(_CloudCurrentGuideTexture);
    TEXTURE2D_X(_CloudHistoryColorTexture);
    TEXTURE2D_X(_CloudHistoryGuideTexture);
    TEXTURE2D_X(_CloudResolvedColorTexture);

    float4 _CloudBlueNoiseParams;
    float4 _CloudHalfResolutionSize;
    float4x4 _CloudPrevViewProj;
    float _CloudHistoryValid;
    float _AerialPerspectiveDistance;
    float4 _AerialPerspectiveVoxelSize;

    #include "VolumetricCloudParams.hlsl"
    #include "VolumetricCloudUtil.hlsl"
    #include "Assets/TodSystem/Atmosphere/Shader/SkyView.hlsl"

    struct Attributes
    {
        uint vertexID : SV_VertexID;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };

    struct Varyings
    {
        float4 positionCS : SV_POSITION;
        float2 texcoord : TEXCOORD0;
        UNITY_VERTEX_OUTPUT_STEREO
    };

    Varyings VertFullscreen(Attributes input)
    {
        Varyings output;
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

        output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
        output.texcoord = GetFullScreenTriangleTexCoord(input.vertexID);
        return output;
    }

    bool IsSkyPixel(float rawDepth)
    {
        #if UNITY_REVERSED_Z
            return rawDepth <= 1e-6;
        #else
            return rawDepth >= 1.0 - 1e-6;
        #endif
    }

    float GetClipDepth(float rawDepth)
    {
        #if UNITY_REVERSED_Z
            return rawDepth;
        #else
            return lerp(UNITY_NEAR_CLIP_VALUE, 1.0, rawDepth);
        #endif
    }

    float CloudOpacity(float4 cloud)
    {
        return saturate(1.0 - cloud.a);
    }

    float CloudLightingLuminance(float4 cloud, float opacity)
    {
        float3 lightingPerOpacity = cloud.rgb / max(opacity, 0.05);
        return dot(lightingPerOpacity, float3(0.2126, 0.7152, 0.0722));
    }

    float OpticalDepthFromTransmittance(float transmittance)
    {
        return -log(max(transmittance, 1e-4));
    }

    float TransmittanceFromOpticalDepth(float opticalDepth)
    {
        return exp(-max(opticalDepth, 0.0));
    }

    float SampleCloudBlueNoise(float2 uv)
    {
        return SAMPLE_TEXTURE2D_LOD(_CloudBlueNoiseTexture, sampler_PointRepeat, uv * _CloudBlueNoiseParams.xy + _CloudBlueNoiseParams.zw, 0).r;
    }

    float4 SampleAerialPerspective(float2 uv, float distanceToCloud)
    {
        if (_AerialPerspectiveDistance <= 1e-4 || _AerialPerspectiveVoxelSize.z <= 1.0)
        {
            return float4(0.0, 0.0, 0.0, 1.0);
        }

        float sliceCount = _AerialPerspectiveVoxelSize.z;
        float distance01 = saturate(distanceToCloud / _AerialPerspectiveDistance);
        float distanceSlice = distance01 * (sliceCount - 1.0);
        float slice = clamp(floor(distanceSlice), 0.0, sliceCount - 1.0);
        float nextSlice = min(slice + 1.0, sliceCount - 1.0);
        float lerpFactor = frac(distanceSlice);

        float sliceUvX = uv.x / sliceCount;
        float2 uv1 = float2(sliceUvX + slice / sliceCount, uv.y);
        float2 uv2 = float2(sliceUvX + nextSlice / sliceCount, uv.y);

        float4 data1 = SAMPLE_TEXTURE2D_LOD(_aerialPerspectiveLut, sampler_LinearClamp, uv1, 0);
        float4 data2 = SAMPLE_TEXTURE2D_LOD(_aerialPerspectiveLut, sampler_LinearClamp, uv2, 0);
        return lerp(data1, data2, lerpFactor);
    }

    // Math:
    //   The sky already contains camera-to-infinity aerial perspective in the destination color.
    //   For the cloud source term we therefore use:
    //   src = T_air * L_cloud + (1 - T_cloud) * I_air
    //   and keep alpha = T_cloud for the existing blend:
    //   out = src + T_cloud * dst.
    // Physical meaning:
    //   Cloud radiance is attenuated by the air between the camera and the cloud, while only the
    //   part of the foreground air that replaces the occluded background sky is added back.
    // Principle:
    //   This avoids double-adding the full aerial in-scattering, because the background sky in dst
    //   already contains the complete camera-path atmosphere contribution.
    // Why it exists:
    //   Without this, the cloud lighting is shaded as if the cloud were at the camera position,
    //   so distant clouds look too crisp and disconnected from the atmosphere depth cue.
        // 数学原理：  
//   目标颜色中已包含从摄像机到无穷远的大气透视效果。  
//   因此，云层源项采用以下公式：  
//   src = T_air * L_cloud + (1 - T_cloud) * I_air  
//   并保持 alpha = T_cloud 用于现有混合：  
//   out = src + T_cloud * dst。  
// 物理意义：  
//   云层辐射被摄像机与云层之间的空气衰减，同时仅将前景空气中替换被遮挡背景天空的部分重新添加回来。  
// 原则：  
//   此方法避免了重复添加完整的大气内散射，因为背景天空（dst）已包含完整的摄像机路径大气贡献。  
// 存在原因：  
//   若不采用此方法，云层光照会如同云层位于摄像机位置般进行着色，导致远处云层显得过于清晰，并失去与大气深度线索的关联性。
    float4 ApplyCloudAerialPerspective(float2 uv, float4 cloud, float3 meanPositionWS)
    {
        float opacity = CloudOpacity(cloud);
        if (opacity <= 1e-4)
        {
            return cloud;
        }

        float distanceToCloud = distance(meanPositionWS, GetCameraPositionWS());
        if (distanceToCloud <= 1e-3)
        {
            return cloud;
        }

        float4 aerialPerspective = SampleAerialPerspective(uv, distanceToCloud);
        cloud.rgb = aerialPerspective.w * cloud.rgb + opacity * aerialPerspective.rgb;
        return cloud;
    }

    struct CloudRawOutput
    {
        float4 color : SV_Target0;
        float4 guide : SV_Target1;
    };

    struct CloudHistoryOutput
    {
        float4 color : SV_Target0;
        float4 guide : SV_Target1;
    };

    ENDHLSL

    SubShader
    {
        Cull Off
        ZTest Always
        ZWrite Off

        Pass
        {
            HLSLPROGRAM

            #pragma vertex VertFullscreen
            #pragma fragment FragRenderCloud
            #pragma target 4.0

            CloudRawOutput FragRenderCloud(Varyings input)
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                CloudRawOutput output;
                output.color = float4(0, 0, 0, 1);
                output.guide = float4(0, 0, 0, 0);

                float2 uv = input.texcoord;
                float rawDepth = SampleSceneDepth(uv);
                if (!IsSkyPixel(rawDepth))
                {
                    return output;
                }

                ViewRay viewRay = CreateViewRayByScreenUV(uv, GetClipDepth(rawDepth));
                float blueNoise = SampleCloudBlueNoise(uv);
                Result result = Calculate(viewRay,
                    blueNoise,
                    _CloudMap, sampler_CloudMap,
                    _NoiseTexture, sampler_NoiseTexture,
                    _CloudLut, sampler_CloudLut);

                output.color = ApplyCloudAerialPerspective(uv, result.ScatteringTransmittance, result.meanPosition);
                output.guide = float4(result.meanPosition, CloudOpacity(result.ScatteringTransmittance));
                return output;
            }

            ENDHLSL
        }

        Pass
        {
            HLSLPROGRAM

            #pragma vertex VertFullscreen
            #pragma fragment FragTemporalAccumulate
            #pragma target 4.0

            CloudHistoryOutput FragTemporalAccumulate(Varyings input)
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;
                float2 texelSize = _CloudHalfResolutionSize.zw;
                float4 currentColor = SAMPLE_TEXTURE2D_X(_CloudCurrentColorTexture, sampler_LinearClamp, uv);
                float4 currentGuide = SAMPLE_TEXTURE2D_X(_CloudCurrentGuideTexture, sampler_LinearClamp, uv);

                CloudHistoryOutput output;
                output.color = currentColor;
                output.guide = currentGuide;

                float currentOpacity = currentGuide.w;
                if (_CloudHistoryValid < 0.5 || currentOpacity <= 1e-4)
                {
                    return output;
                }

                float4 prevClip = mul(_CloudPrevViewProj, float4(currentGuide.xyz, 1.0));
                if (prevClip.w <= 1e-5)
                {
                    return output;
                }

                float2 prevUv = prevClip.xy / prevClip.w * 0.5 + 0.5;
                if (any(prevUv < 0.0) || any(prevUv > 1.0))
                {
                    return output;
                }

                float4 historyColor = SAMPLE_TEXTURE2D_X(_CloudHistoryColorTexture, sampler_LinearClamp, prevUv);
                float4 historyGuide = SAMPLE_TEXTURE2D_X(_CloudHistoryGuideTexture, sampler_LinearClamp, prevUv);
                float historyOpacity = historyGuide.w;
                if (historyOpacity <= 1e-4)
                {
                    return output;
                }

                if (distance(currentGuide.xyz, historyGuide.xyz) > _TemporalWorldPosThreshold)
                {
                    return output;
                }

                if (abs(currentOpacity - historyOpacity) > _TemporalOpacityThreshold)
                {
                    return output;
                }

                float currentLuminance = CloudLightingLuminance(currentColor, currentOpacity);
                float historyLuminance = CloudLightingLuminance(historyColor, historyOpacity);
                if (abs(currentLuminance - historyLuminance) > _TemporalLuminanceThreshold)
                {
                    return output;
                }

                float3 minScattering = currentColor.rgb;
                float3 maxScattering = currentColor.rgb;
                float currentOpticalDepth = OpticalDepthFromTransmittance(currentColor.a);
                float minOpticalDepth = currentOpticalDepth;
                float maxOpticalDepth = currentOpticalDepth;

                [unroll]
                for (int y = -1; y <= 1; ++y)
                {
                    [unroll]
                    for (int x = -1; x <= 1; ++x)
                    {
                        float2 sampleUv = uv + float2(x, y) * texelSize;
                        float4 neighborhoodColor = SAMPLE_TEXTURE2D_X(_CloudCurrentColorTexture, sampler_LinearClamp, sampleUv);
                        float neighborhoodOpticalDepth = OpticalDepthFromTransmittance(neighborhoodColor.a);

                        minScattering = min(minScattering, neighborhoodColor.rgb);
                        maxScattering = max(maxScattering, neighborhoodColor.rgb);
                        minOpticalDepth = min(minOpticalDepth, neighborhoodOpticalDepth);
                        maxOpticalDepth = max(maxOpticalDepth, neighborhoodOpticalDepth);
                    }
                }

                float3 clampedHistoryScattering = clamp(historyColor.rgb, minScattering, maxScattering);
                float historyOpticalDepth = OpticalDepthFromTransmittance(historyColor.a);
                float clampedHistoryOpticalDepth = clamp(historyOpticalDepth, minOpticalDepth, maxOpticalDepth);
                float clampStrength = saturate(_TemporalClampStrength);

                float3 filteredHistoryScattering = lerp(historyColor.rgb, clampedHistoryScattering, clampStrength);
                float filteredHistoryOpticalDepth = lerp(historyOpticalDepth, clampedHistoryOpticalDepth, clampStrength);
                float historyWeight = saturate(_TemporalHistoryWeight);

                output.color = float4(
                    lerp(currentColor.rgb, filteredHistoryScattering, historyWeight),
                    TransmittanceFromOpticalDepth(lerp(currentOpticalDepth, filteredHistoryOpticalDepth, historyWeight)));
                return output;
            }

            ENDHLSL
        }

        Pass
        {
            Blend One SrcAlpha, Zero One

            HLSLPROGRAM

            #pragma vertex VertFullscreen
            #pragma fragment FragResolveComposite
            #pragma target 4.0

            float4 FragResolveComposite(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;
                float rawDepth = SampleSceneDepth(uv);
                if (!IsSkyPixel(rawDepth))
                {
                    return float4(0, 0, 0, 1);
                }

                float2 halfSize = _CloudHalfResolutionSize.xy;
                float2 halfTexelSize = _CloudHalfResolutionSize.zw;
                float2 halfPixel = uv * halfSize - 0.5;
                float2 basePixel = floor(halfPixel);
                float2 fracCoord = saturate(halfPixel - basePixel);

                float3 accumScattering = 0;
                float accumOpticalDepth = 0;
                float weightSum = 0;

                [unroll]
                for (int y = 0; y <= 1; ++y)
                {
                    [unroll]
                    for (int x = 0; x <= 1; ++x)
                    {
                        float2 tapPixel = basePixel + float2(x, y) + 0.5;
                        float2 sampleUv = tapPixel * halfTexelSize;
                        if (any(sampleUv < 0.0) || any(sampleUv > 1.0))
                        {
                            continue;
                        }

                        if (!IsSkyPixel(SampleSceneDepth(sampleUv)))
                        {
                            continue;
                        }

                        float bilinearWeightX = (x == 0) ? (1.0 - fracCoord.x) : fracCoord.x;
                        float bilinearWeightY = (y == 0) ? (1.0 - fracCoord.y) : fracCoord.y;
                        float weight = bilinearWeightX * bilinearWeightY;
                        float4 cloud = SAMPLE_TEXTURE2D_X(_CloudResolvedColorTexture, sampler_LinearClamp, sampleUv);

                        accumScattering += cloud.rgb * weight;
                        accumOpticalDepth += OpticalDepthFromTransmittance(cloud.a) * weight;
                        weightSum += weight;
                    }
                }

                if (weightSum <= 1e-5)
                {
                    return float4(0, 0, 0, 1);
                }

                return float4(accumScattering / weightSum, TransmittanceFromOpticalDepth(accumOpticalDepth / weightSum));
            }

            ENDHLSL
        }
    }
}
