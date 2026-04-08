// Despill.hlsl - Graduated despill: suppress target color cast near keyed edges
cbuffer Params : register(b0)
{
    float3 targetNormRGB; // target color normalized (0-1)
    int2 dims;
};

Texture2D<float> alphaTexture : register(t0);
RWTexture2D<float4> dstTexture : register(u0);

// Use groupshared memory for 13x13 neighborhood alpha lookup
// Thread group = 8x8, halo = 6 each side, tile = 20x20
groupshared float sharedAlpha[20][20];

[numthreads(8, 8, 1)]
void CSMain(uint3 groupId : SV_GroupID, uint3 localId : SV_GroupThreadID, uint3 globalId : SV_DispatchThreadID)
{
    // Load tile into shared memory (each thread loads multiple cells)
    int2 tileOrigin = int2(groupId.xy) * 8 - 6; // 6 = halo
    for (int ty = (int)localId.y; ty < 20; ty += 8)
        for (int tx = (int)localId.x; tx < 20; tx += 8)
        {
            int2 pos = tileOrigin + int2(tx, ty);
            if (pos.x >= 0 && pos.x < dims.x && pos.y >= 0 && pos.y < dims.y)
                sharedAlpha[ty][tx] = alphaTexture[pos];
            else
                sharedAlpha[ty][tx] = 1.0f;
        }
    GroupMemoryBarrierWithGroupSync();

    if (globalId.x >= (uint)dims.x || globalId.y >= (uint)dims.y) return;

    // Only process pixels near keyed region
    int2 localPos = int2(localId.xy) + 6; // offset by halo
    float myAlpha = sharedAlpha[localPos.y][localPos.x];
    if (myAlpha < 0.9f) return;

    // Find minimum distance to keyed pixel in 13x13 window
    int minDist = 0x7FFFFFFF;
    [unroll] for (int dy = -6; dy <= 6; dy++)
        [unroll] for (int dx = -6; dx <= 6; dx++)
        {
            if (sharedAlpha[localPos.y + dy][localPos.x + dx] < 0.5f)
            {
                int d = abs(dx) + abs(dy);
                minDist = min(minDist, d);
            }
        }
    if (minDist > 6) return;

    float strength = saturate(1.0f - (float)(minDist - 1) / 6.0f) * 0.7f;

    float4 px = dstTexture[globalId.xy];
    float3 c = px.rgb;

    if (targetNormRGB.r >= targetNormRGB.g && targetNormRGB.r >= targetNormRGB.b)
    {
        float limit = max(c.g, c.b);
        if (c.r > limit) c.r = c.r * (1.0f - strength) + limit * strength;
    }
    else if (targetNormRGB.g >= targetNormRGB.r && targetNormRGB.g >= targetNormRGB.b)
    {
        float limit = max(c.r, c.b);
        if (c.g > limit) c.g = c.g * (1.0f - strength) + limit * strength;
    }
    else
    {
        float limit = max(c.r, c.g);
        if (c.b > limit) c.b = c.b * (1.0f - strength) + limit * strength;
    }

    dstTexture[globalId.xy] = float4(c, px.a);
}
