// MaskLabMask.hlsl - Binary mask creation + conditional dilation
cbuffer Params : register(b0)
{
    float3 targetLab;
    float targetChroma;
    float targetAngle;
    float maxDist;       // threshold.H * 1.35
    int2 dims;
    int dilationPass;    // 0=initial mark, 1-3=dilation passes
};

StructuredBuffer<float3> labLut : register(t0);
Texture2D<float4> srcTexture : register(t1);
Texture2D<float> alphaIn : register(t2);    // read (ping)
RWTexture2D<float> alphaOut : register(u0); // write (pong)

static const float PI = 3.14159265f;

float3 rgbToLabFast(float3 rgb)
{
    int ri = (int)(rgb.r * 255.0f) >> 2;
    int gi = (int)(rgb.g * 255.0f) >> 2;
    int bi = (int)(rgb.b * 255.0f) >> 2;
    return labLut[(ri << 12) | (gi << 6) | bi];
}

[numthreads(8, 8, 1)]
void CSMain(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= (uint)dims.x || id.y >= (uint)dims.y) return;

    if (dilationPass == 0)
    {
        // Initial mark pass
        float4 src = srcTexture[id.xy];
        float3 lab = rgbToLabFast(src.rgb);
        float dA = lab.y - targetLab.y, dB = lab.z - targetLab.z;
        float cd = sqrt(dA * dA + dB * dB);
        float pc = sqrt(lab.y * lab.y + lab.z * lab.z);

        bool dm = cd <= maxDist;
        bool em = false;
        if (pc > 2.0f && targetChroma > 5.0f)
        {
            float pa = atan2(lab.z, lab.y);
            float ad = abs(targetAngle - pa);
            if (ad > PI) ad = 2.0f * PI - ad;
            em = ad * 180.0f / PI <= maxDist * 1.3f;
        }
        alphaOut[id.xy] = (dm || em) ? 0.0f : 1.0f;
    }
    else
    {
        // Dilation pass
        float cur = alphaIn[id.xy];
        if (cur < 0.5f) { alphaOut[id.xy] = 0.0f; return; } // already keyed

        int x = (int)id.x, y = (int)id.y;
        if (x < 1 || x >= dims.x - 1 || y < 1 || y >= dims.y - 1)
        { alphaOut[id.xy] = cur; return; }

        int nb = 0;
        if (alphaIn[int2(x, y-1)] < 0.5f) nb++;
        if (alphaIn[int2(x, y+1)] < 0.5f) nb++;
        if (alphaIn[int2(x-1, y)] < 0.5f) nb++;
        if (alphaIn[int2(x+1, y)] < 0.5f) nb++;
        if (nb < 3) { alphaOut[id.xy] = cur; return; }

        float4 src = srcTexture[id.xy];
        float3 lab = rgbToLabFast(src.rgb);
        float ec = sqrt(lab.y * lab.y + lab.z * lab.z);
        if (ec < 1.5f) { alphaOut[id.xy] = cur; return; }
        float ea = atan2(lab.z, lab.y);
        float ad = abs(targetAngle - ea);
        if (ad > PI) ad = 2.0f * PI - ad;
        alphaOut[id.xy] = (ad * 180.0f / PI <= 28.0f) ? 0.0f : cur;
    }
}
