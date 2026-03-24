Shader"TOD/Atmosphere/AerialPerspectiveLut"
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/UnityInput.hlsl"
            
            #include "SkyView.hlsl"
            
            float _AerialPerspectiveDistance;
            float4 _AerialPerspectiveVoxelSize;

            // SAMPLER(sampler_LinearClamp);
            Texture2D _transmittanceLut;
            Texture2D _multiScatteringLut;

            struct Attributes
            {
                uint vertexID : SV_VertexID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
            };
            
            Varyings Vert(Attributes input)
            {
                Varyings output;

                if (input.vertexID == 0)
                {
                    output.positionCS = float4(-1.0, 1.0, 0.0, 1.0);
                    output.uv = float2(0.0, 0.0);
                }
                else if (input.vertexID == 1)
                {
                    output.positionCS = float4(-1.0, -3.0, 0.0, 1.0);
                    output.uv = float2(0.0, 2.0);
                }
                else
                {
                    output.positionCS = float4( 3.0, 1.0, 0.0, 1.0);
                    output.uv = float2(2.0, 0.0);
                }
                //这时，屏幕左下角uv为（0,0），右上角uv为（1,1）
                return output;
            }
            
            float4 Frag(Varyings input) : SV_Target
            {
                AtmosphereParams param = GetAtmosphereParameter();
                
                float4 color = float4(0, 0, 0, 1);
                float3 uv = float3(input.uv, 0);
                uv.x *= _AerialPerspectiveVoxelSize.x * _AerialPerspectiveVoxelSize.z;  // X * Z
                uv.z = int(uv.x / _AerialPerspectiveVoxelSize.x) / _AerialPerspectiveVoxelSize.z;
                uv.x = fmod(uv.x, _AerialPerspectiveVoxelSize.x) / _AerialPerspectiveVoxelSize.x;
                uv.xyz += 0.5 / _AerialPerspectiveVoxelSize.xyz;

                float2 ndc = uv.xy * 2 - 1.0f;
                // return float4(ndc,0,1);
                float4 posCS = float4(ndc,1.0,1.0);
                float4 viewDirVS = mul(unity_CameraInvProjection, posCS);
                viewDirVS = normalize( (viewDirVS/viewDirVS.w));
                float3 viewDirWS = mul((float3x3)unity_CameraToWorld, viewDirVS);
                viewDirWS = normalize(viewDirWS);
                

                Light mainLight = GetMainLight();
                float3 lightDir = mainLight.direction;
                
                float h = _WorldSpaceCameraPos.y - param.SeaLevel + param.PlanetRadius;
                float3 eyePos = float3(0, h, 0);

                float maxDis = uv.z * _AerialPerspectiveDistance;

                // inScattering
                color.rgb = GetSkyView(
                    param, eyePos, viewDirWS, lightDir, maxDis,
                    _transmittanceLut, _multiScatteringLut, sampler_LinearClamp
                );

                // transmittance
                float3 voxelPos = eyePos + viewDirWS * maxDis;
                float3 t1 = TransmittanceToAtmosphereByLut(param, eyePos, viewDirWS, _transmittanceLut, sampler_LinearClamp);
                float3 t2 = TransmittanceToAtmosphereByLut(param, voxelPos, viewDirWS, _transmittanceLut, sampler_LinearClamp);
                float3 t = t1 / t2;
                color.a = dot(t, float3(1.0 / 3.0, 1.0 / 3.0, 1.0 / 3.0));

                return color;
            }

            
            ENDHLSL
        }
    }
}
