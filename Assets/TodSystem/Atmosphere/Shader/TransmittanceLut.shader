Shader "TOD/Atmosphere/TransmittanceLut"
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

            #include "Transmittance.hlsl"
            #include "Helper.hlsl"
            
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

            float4 Frag ( Varyings input ): SV_Target
            {
                AtmosphereParams params = GetAtmosphereParameter();

                float4 color = float4(0,0,0,1);
                float2 uv = input.uv;

                float bottomRadius = params.PlanetRadius;
                float topRadius = params.PlanetRadius + params.AtmosphereHeight;

                float cos_theta = 0.0;
                float r = 0.0;
                GetTransmittanceLutParamsFromUV(bottomRadius, topRadius, uv, cos_theta, r);

                float sin_theta = sqrt(1 -cos_theta * cos_theta);
                float3 viewDir = float3(sin_theta, cos_theta ,0);
                float3 eyePos = float3(0, r, 0);

                float distance = RayIntersectSphereLength(float3(0,0,0),params.PlanetRadius + params.AtmosphereHeight, eyePos, viewDir);

                float3 hitPoint = eyePos + distance * viewDir;

                color.rgb = Transmittance(params, eyePos,hitPoint);
                
                //return float4(uv,0,1);
                return color;

            }    
            
            ENDHLSL
        }
    }
}