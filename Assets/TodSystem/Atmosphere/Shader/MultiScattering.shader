Shader "TOD/Atmosphere/MultiScatteringLut"
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

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "AtmosphereParams.hlsl"
            #include "MultiScattering.hlsl"
            

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

            float4 Frag (Varyings input) : SV_Target
            {
                AtmosphereParams param = GetAtmosphereParameter();

                float4 color = float4(0, 0, 0, 1);
                float2 uv = input.uv;

                float mu_s = uv.x * 2.0 - 1.0;
                float r = uv.y * param.AtmosphereHeight + param.PlanetRadius;

                float cos_theta = mu_s;
                float sin_theta = sqrt(1.0 - cos_theta * cos_theta);
                float3 lightDir = float3(sin_theta, cos_theta, 0);
                float3 p = float3(0, r, 0);

                color.rgb = IntegralMultiScattering(param, p, lightDir, _transmittanceLut, sampler_LinearClamp);
                //color.rg = uv;
                // return float4(uv, 0, 1);
                return color ;
            }
            ENDHLSL
        }
    }
}
