Shader"TOD/VolumetricCloud/VolumetricCloud"
{
    Properties
    {
        [NoScaleOffset]_CloudMap("Cloud Map", 2D) = "white"{}
        [NoScaleOffset]_NoiseTexture("3D Noise",3D) = "defaulttexture"{}
        [NoScaleOffset]_CloudLut("Cloud Lut", 2D) = "white"{}
    }
    
    SubShader
    {
       Cull Off
       ZTest Always
       ZWrite Off
       
       Pass
       {
           HLSLPROGRAM
           
           #pragma vertex Vert
           #pragma fragment Frag
            
           #pragma  target 4.0

           
           #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
           #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
           #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
           

           
           TEXTURE2D(_CloudMap);
           SAMPLER(sampler_CloudMap);
           TEXTURE3D(_NoiseTexture);
           SAMPLER(sampler_NoiseTexture);
           TEXTURE2D(_CloudLut);
           SAMPLER(sampler_CloudLut);
           //
           TEXTURE2D(_transmittanceLut);
           SAMPLER(sampler_transmittanceLut);
           TEXTURE2D(_aerialPerspectiveLut);
           SAMPLER(sampler_aerialPerspectiveLut);
           TEXTURE2D(_multiScatteringLut);
           
           


           #include "VolumetricCloudUtil.hlsl"
           #include "Assets/TodSystem/Atmosphere/Shader/SkyView.hlsl"


           
           float4 Frag(Varyings input):SV_Target
           {
               float4 baseColor = SAMPLE_TEXTURE2D(_BlitTexture,sampler_LinearClamp,input.texcoord);
               float2 uv = input.texcoord;
               float rawDepth = SampleSceneDepth(uv);
               #if UNITY_REVERSED_Z
                     float depth = rawDepth;
                     bool isSkyPixel = rawDepth <= 1e-6;
               #else
                // Adjust z to match NDC for OpenGL
                    float depth = lerp(UNITY_NEAR_CLIP_VALUE, 1, rawDepth);
                    bool isSkyPixel = rawDepth >= 1.0 - 1e-6;
               #endif
               if (!isSkyPixel)
               {
                   return float4(baseColor.rgb,1);
               }
               
               ViewRay viewRay = CreateViewRayByScreenUV(uv,depth);
               Result Res;
               Res = Calculate(viewRay, uv,
                   _CloudMap, sampler_CloudMap,
                   _NoiseTexture,sampler_NoiseTexture,
                   _CloudLut,sampler_CloudLut); 
               float4 ScatteringTransmittance = Res.ScatteringTransmittance;
               float3 fin = ScatteringTransmittance.rgb + ScatteringTransmittance.a * baseColor.rgb;
               float3 meanPosition = Res.meanPosition;
   
                //顺带一提，对云做空气透视（atmosphere里那套实现）能把云变成紫色，不要问我怎么知道的（
               return float4(fin,1);
           }
           
           
       
           ENDHLSL
       }
    }
    
}
