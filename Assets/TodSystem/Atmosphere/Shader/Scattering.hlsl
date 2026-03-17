#ifndef SCATTERING_HLSL
#define SCATTERING_HLSL
    
#include "AtmosphereParams.hlsl"
#include "Helper.hlsl"

//瑞利散射的散射系数
float3 RayleighCoefficient(in AtmosphereParams params, float height)
{
    const float3 sigma = float3(5.802, 13.558, 33.1) * 1e-6;
    float scalarHeight = params.RayleighScatteringScalarHeight;
    float rho_h = exp(-(height / scalarHeight));
    return sigma * rho_h * params.RayleighScatteringScale;
}
//瑞利散射相位函数
float RayleighPhase(float cos_theta)
{
    return (3.0 / (16.0 * PI)) * (1.0 + cos_theta * cos_theta);
}
//米氏散射的散射系数
float3 MieCoefficient(in AtmosphereParams params, float height)
{
    const float3 sigma = (3.996 * 1e-6).xxx;
    float scalarHeight = params.MieScatteringScalarHeight;
    float rho_h = exp(-(height/scalarHeight));
    return sigma * rho_h * params.MieScatteringScale;
}
//米氏散射相位函数
float MiePhase(in AtmosphereParams param, float cos_theta)
{
    float g = param.MieAnisotropy;

    float a = 3.0/(8.0 * PI);
    float b = (1-g*g)/(2+g*g);
    float c = 1.0 + cos_theta * cos_theta;
    float d = pow(1.0 + g*g - 2*g*cos_theta, 1.5);

    return a * b * c / d ;
}

//点散射（通常我们认为任意点均匀的发生两种散射，这是一种便于计算的假设）
float3 ScatteringOnPoint(in AtmosphereParams params, float3 position, float3 inDirection, float3 outDirection)
{

    float cos_theta = dot(normalize(inDirection), normalize(outDirection));

    float height = length(position) - params.PlanetRadius;
    float3 rayleigh = RayleighCoefficient(params, height) * RayleighPhase(cos_theta);
    float3 mie = MieCoefficient(params, height) * MiePhase(params, cos_theta);

    return  rayleigh + mie;
    
}
//米氏散射吸收
float3 MieAbsorption(in AtmosphereParams params, float height)
{
    const float3 sigma = (4.4 * 1e-6).xxx;
    float H_M = params.MieScatteringScalarHeight;
    float rho_h = exp(-(height / H_M));
    return sigma * rho_h;
}
//臭氧层的吸收
float3 OzoneAbsorption(in AtmosphereParams params, float height)
{
    const float3 sigma_ozone = (float3(0.650f, 1.881f, 0.085f)) * 1e-6;
    float center = params.OzoneLevelCenterHeight;
    float width = params.OzoneLevelWidth;
    float rho = max(0, 1.0 - (abs(height - center) / width));
    return sigma_ozone * rho * params.OzoneAbsorptionScale;
}



#endif