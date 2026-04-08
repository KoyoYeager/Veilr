// JfaInit.hlsl - Initialize JFA seed map from alpha texture
cbuffer Params : register(b0)
{
    int2 dims;
};

Texture2D<float> alphaTexture : register(t0);
Texture2D<float4> srcTexture : register(t1);
RWTexture2D<int2> seedMap : register(u0);
RWTexture2D<float4> bgColor : register(u1);

[numthreads(8, 8, 1)]
void CSMain(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= (uint)dims.x || id.y >= (uint)dims.y) return;

    float alpha = alphaTexture[id.xy];

    // Check if this is a "clean" pixel (not adjacent to any keyed pixel)
    bool clean = (alpha >= 1.0f);
    if (clean)
    {
        [unroll] for (int dy = -1; dy <= 1; dy++)
            [unroll] for (int dx = -1; dx <= 1; dx++)
            {
                int2 n = int2(id.xy) + int2(dx, dy);
                if (n.x >= 0 && n.x < dims.x && n.y >= 0 && n.y < dims.y)
                    if (alphaTexture[n] < 1.0f) clean = false;
            }
    }

    if (clean)
    {
        seedMap[id.xy] = int2(id.xy);
        bgColor[id.xy] = srcTexture[id.xy];
    }
    else
    {
        seedMap[id.xy] = int2(-1, -1);
        bgColor[id.xy] = float4(1, 1, 1, 1); // fallback white
    }
}
