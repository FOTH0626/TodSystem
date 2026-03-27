Shader"TOD/Atmosphere/AerialPerspective"
{
    Properties {}
    
    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always
        Blend Off
        
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            


            Texture2D _aerialPerspectiveLut;
            Texture2D _transmittanceLut;
            Texture2D _skyViewLut;
            
            CBUFFER_START(UnityPerMaterial)
            float _AerialPerspectiveDistance;
            float4 _AerialPerspectiveVoxelSize;
            CBUFFER_END
            
            
            #include"Assets/TodSystem/Atmosphere/Shader/Helper.hlsl"
            
            float4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;

                float3 sceneColor = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;
                float sceneRawDepth = SampleSceneDepth(uv);
                
                // float3 color = smoothstep(float3(1,0,0), float3(0,0,1),sceneRawDepth.xxx * 200);
                // return float4(color,1);
                //无限远处（天空盒）不参与空气透视
            #if UNITY_REVERSED_Z
                if(sceneRawDepth == 0.0f) return float4(sceneColor, 1.0);
            #else
                if(sceneRawDepth == 1.0f) return float4(sceneColor, 1.0);
            #endif
                //return float4(1.0, 0, 0, 1);
                
                // 世界坐标计算
                float3 worldPos = GetFragmentWorldPos(input.texcoord).xyz;
                float3 eyePos = _WorldSpaceCameraPos.xyz;
                float dis = length(worldPos - eyePos);
                float3 viewDir = normalize(worldPos - eyePos);

                // 体素 slice 计算
                //TODO: 使用平方映射可以提高近处精度
                float dis01 = saturate(dis / _AerialPerspectiveDistance);
                float dis0Z = dis01 * (_AerialPerspectiveVoxelSize.z - 1); 
                float slice = clamp( floor(dis0Z), 0.0, _AerialPerspectiveVoxelSize.z - 1); 
                float nextSlice = min(slice + 1, _AerialPerspectiveVoxelSize.z - 1);
                float lerpFactor = frac(dis0Z);

                uv.x /= _AerialPerspectiveVoxelSize.z;//除以切片数量

                // 采样 AerialPerspectiveVoxel
                float2 uv1 = float2(uv.x + slice / _AerialPerspectiveVoxelSize.z, uv.y);
                float2 uv2 = float2(uv.x + nextSlice / _AerialPerspectiveVoxelSize.z, uv.y);

                float4 data1 = _aerialPerspectiveLut.SampleLevel(sampler_LinearClamp, uv1, 0);
                float4 data2 = _aerialPerspectiveLut.SampleLevel(sampler_LinearClamp, uv2, 0);
                float4 data = lerp(data1, data2, lerpFactor);

                float3 inScattering = data.xyz;
                float transmittance = data.w;
                
                return float4(sceneColor * transmittance + inScattering, 1.0);
            }
            
            ENDHLSL
        }
    }
}