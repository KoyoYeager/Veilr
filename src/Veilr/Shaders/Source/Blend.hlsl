// Blend.hlsl — Alpha blend: dst = src * alpha + bg * (1 - alpha)
cbuffer Params : register(b0)
{
    int2 dims;
};

Texture2D<float4> srcTexture : register(t0);
Texture2D<float> alphaTexture : register(t1);
Texture2D<float4> bgColor : register(t2);
RWTexture2D<float4> dstTexture : register(u0);

[numthreads(8, 8, 1)]
void CSMain(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= (uint)dims.x || id.y >= (uint)dims.y) return;

    float a = alphaTexture[id.xy];
    float4 src = srcTexture[id.xy];

    if (a >= 1.0f)
    {
        dstTexture[id.xy] = src;
    }
    else
    {
        float4 bg = bgColor[id.xy];
        dstTexture[id.xy] = float4(src.rgb * a + bg.rgb * (1.0f - a), 1.0f);
    }
}
