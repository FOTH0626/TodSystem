Shader "Atmosphere/SkyViewLut"
{
    
    Properties
    {

    }
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

            #include "SkyView.hlsl"



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
                Varyings o;

                if (input.vertexID == 0)
                {
                    o.positionCS = float4(-1.0, 1.0, 0.0, 1.0);
                    o.uv = float2(0.0, 0.0);
                }
                else if (input.vertexID == 1)
                {
                    o.positionCS = float4(-1.0, -3.0, 0.0, 1.0);
                    o.uv = float2(0.0, 2.0);
                }
                else
                {
                    o.positionCS = float4( 3.0, 1.0, 0.0, 1.0);
                    o.uv = float2(2.0, 0.0);
                }
                //这时，屏幕左下角uv为（0,0），右上角uv为（1,1）
                return o;
            }

            Texture2D _transmittanceLut;
            Texture2D _multiScatteringLut;
 
            float4 Frag (Varyings input) : SV_Target
            {
                AtmosphereParams params = GetAtmosphereParameter();

                float4 color = float4(0, 0, 0, 1);
                float2 uv = input.uv;
                float3 viewDir = UVToViewDir(uv);

                Light mainLight = GetMainLight();
                float3 lightDir = mainLight.direction;
                
                
                float h = _WorldSpaceCameraPos.y - params.SeaLevel + params.PlanetRadius;
                float3 eyePos = float3(0, h, 0);

                color.rgb = GetSkyView(
                    params, eyePos, viewDir, lightDir, -1.0f,
                    _transmittanceLut, _multiScatteringLut, sampler_LinearClamp
                );
            
                return color;
            }
            ENDHLSL
        }
    }
}
