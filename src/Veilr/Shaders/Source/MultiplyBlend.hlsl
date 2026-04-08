// MultiplyBlend.hlsl - Sheet mode: dst = src * sheetColor / 255
cbuffer Params : register(b0)
{
    float4 sheetColor;  // .xyz = R,G,B normalized (0-1), .w unused
    int2 dims;          // width, height
};

Texture2D<float4> srcTexture : register(t0);
RWTexture2D<float4> dstTexture : register(u0);

[numthreads(8, 8, 1)]
void CSMain(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= (uint)dims.x || id.y >= (uint)dims.y) return;
    float4 src = srcTexture[id.xy];
    dstTexture[id.xy] = float4(src.rgb * sheetColor.xyz, src.a);
}
