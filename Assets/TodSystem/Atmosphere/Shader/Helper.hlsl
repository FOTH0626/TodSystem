#ifndef HELPER_HLSL
#define HELPER_HLSL

#ifndef PI 
#define PI 3.1415926535f
#endif

 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

float RayIntersectSphereLength(float3 center, float radius, float3 rayOrigin, float3 rayDirection)
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

void GetTransmittanceLutParamsFromUV(float bottomRadius, float topRadius, float2 uv, out float mu, out float r)
{
    float x_mu = uv.x;
    float x_r = uv.y;

    float H = sqrt(max(0.0f, topRadius * topRadius - bottomRadius * bottomRadius));
    float rho = H * x_r;
    r = sqrt(max(0.0f, rho * rho + bottomRadius * bottomRadius));

    float d_min = topRadius - r;
    float d_max = rho + H;
    float d = d_min + x_mu * (d_max - d_min);
    mu = d == 0.0f ? 1.0f : (H * H - rho * rho - d * d) / (2.0f * r * d);
    mu = clamp(mu, -1.0f, 1.0f);
}

float2 GetTransmittanceLutUvFromParams(float bottomRadius, float topRadius, float mu, float r)
{
    float H = sqrt(max(0.0f, topRadius * topRadius - bottomRadius * bottomRadius));
    float rho = sqrt(max(0.0f, r * r - bottomRadius * bottomRadius));

    float discriminant = r * r * (mu * mu - 1.0f) + topRadius * topRadius;
    float d = max(0.0f, (-r * mu + sqrt(discriminant)));

    float d_min = topRadius - r;
    float d_max = rho + H;

    float x_mu = (d - d_min) / (d_max - d_min);
    float x_r = rho / H;

    return float2(x_mu, x_r);
}

float3 UVToViewDir(float2 uv)
{
    float theta = (1.0 - uv.y) * PI;
    float phi = (uv.x * 2 - 1) * PI;
    
    float x = sin(theta) * cos(phi);
    float z = sin(theta) * sin(phi);
    float y = cos(theta);

    return float3(x, y, z);
}

float2 ViewDirToUV(float3 v)
{
    float2 uv = float2(atan2(v.z, v.x), asin(v.y));
    uv /= float2(2.0 * PI, PI);
    uv += float2(0.5, 0.5);

    return uv; 
}

float3 GetFragmentWorldPos(float2 uv)
{
    float depth;
    #if UNITY_REVERSED_Z
    depth = SampleSceneDepth(uv);
    #else
    depth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, SampleSceneDepth(uv));
    #endif

    return ComputeWorldSpacePosition(uv, depth, UNITY_MATRIX_I_VP);
}

#endif
