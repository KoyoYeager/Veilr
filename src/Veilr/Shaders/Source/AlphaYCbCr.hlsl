// AlphaYCbCr.hlsl - Pass 1: YCbCr color distance to soft alpha
cbuffer Params : register(b0)
{
    float targetCb;
    float targetCr;
    float similarity;
    float smoothness;
    float outerRadius;
    float hueTolerance;
    float targetAngleCbCr;
    float targetChromaCbCr;
    int2 dims;
};

Texture2D<float4> srcTexture : register(t0);
RWTexture2D<float> alphaOut : register(u0);

static const float PI = 3.14159265f;

[numthreads(8, 8, 1)]
void CSMain(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= (uint)dims.x || id.y >= (uint)dims.y) return;

    float4 src = srcTexture[id.xy];
    float r = src.r * 255.0f, g = src.g * 255.0f, b = src.b * 255.0f;

    float pCb = -0.169f * r - 0.331f * g + 0.500f * b + 128.0f;
    float pCr =  0.500f * r - 0.419f * g - 0.081f * b + 128.0f;

    float dist = sqrt((pCb - targetCb) * (pCb - targetCb) + (pCr - targetCr) * (pCr - targetCr));

    float pCbC = pCb - 128.0f, pCrC = pCr - 128.0f;
    float pChroma = sqrt(pCbC * pCbC + pCrC * pCrC);
    if (pChroma > 5.0f && targetChromaCbCr > 5.0f)
    {
        float pAngle = atan2(pCrC, pCbC);
        float aDiff = abs(targetAngleCbCr - pAngle);
        if (aDiff > PI) aDiff = 2.0f * PI - aDiff;
        float aDeg = aDiff * 180.0f / PI;
        if (aDeg <= hueTolerance && pChroma > 8.0f)
            dist = min(dist, aDeg / hueTolerance * similarity);
    }

    float alpha;
    if (dist <= similarity) alpha = 0.0f;
    else if (dist >= outerRadius) alpha = 1.0f;
    else alpha = (dist - similarity) / smoothness;

    alphaOut[id.xy] = alpha;
}
