// AlphaChromaKey.hlsl - Pass 1: Lab color distance to soft alpha (0.0-1.0)
cbuffer Params : register(b0)
{
    float3 targetLab;       // target L, a, b
    float targetChroma;     // sqrt(a*a+b*b)
    float targetAngle;      // atan2(b, a)
    float similarity;       // inner radius
    float smoothness;       // transition width
    float outerRadius;      // similarity + smoothness
    float hueToleranceDeg;  // similarity * 0.5
    int2 dims;
};

StructuredBuffer<float3> labLut : register(t0); // 64^3 entries
Texture2D<float4> srcTexture : register(t1);
RWTexture2D<float> alphaOut : register(u0);

static const float PI = 3.14159265f;

float3 rgbToLabFast(float3 rgb)
{
    // 6-bit quantize (0-255 to 0-63)
    int ri = (int)(rgb.r * 255.0f) >> 2;
    int gi = (int)(rgb.g * 255.0f) >> 2;
    int bi = (int)(rgb.b * 255.0f) >> 2;
    int idx = (ri << 12) | (gi << 6) | bi;
    return labLut[idx];
}

[numthreads(8, 8, 1)]
void CSMain(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= (uint)dims.x || id.y >= (uint)dims.y) return;

    float4 src = srcTexture[id.xy];
    float3 lab = rgbToLabFast(src.rgb);

    float dA = lab.y - targetLab.y;
    float dB = lab.z - targetLab.z;
    float chromaDist = sqrt(dA * dA + dB * dB);
    float pixelChroma = sqrt(lab.y * lab.y + lab.z * lab.z);

    float keyDist = chromaDist;

    if (pixelChroma > 2.0f && targetChroma > 5.0f)
    {
        float pixelAngle = atan2(lab.z, lab.y);
        float angleDiff = abs(targetAngle - pixelAngle);
        if (angleDiff > PI) angleDiff = 2.0f * PI - angleDiff;
        float angleDeg = angleDiff * 180.0f / PI;

        if (angleDeg <= hueToleranceDeg && pixelChroma > 10.0f)
            keyDist = min(keyDist, angleDeg / hueToleranceDeg * similarity);
        else if (angleDeg <= outerRadius * 1.3f)
        {
            float angleBonus = (1.0f - angleDeg / (outerRadius * 1.3f)) * similarity * 0.5f;
            keyDist = max(0.0f, keyDist - angleBonus);
        }
    }

    float alpha;
    if (keyDist <= similarity) alpha = 0.0f;
    else if (keyDist >= outerRadius) alpha = 1.0f;
    else alpha = (keyDist - similarity) / smoothness;

    alphaOut[id.xy] = alpha;
}
