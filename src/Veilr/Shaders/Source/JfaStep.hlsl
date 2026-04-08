// JfaStep.hlsl - One JFA propagation step (dispatch log2(max(w,h)) times)
cbuffer Params : register(b0)
{
    int stepSize;
    int2 dims;
};

Texture2D<int2> seedMapIn : register(t0);
Texture2D<float4> srcTexture : register(t1);
RWTexture2D<int2> seedMapOut : register(u0);
RWTexture2D<float4> bgColor : register(u1);

[numthreads(8, 8, 1)]
void CSMain(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= (uint)dims.x || id.y >= (uint)dims.y) return;

    int2 bestSeed = seedMapIn[id.xy];
    int bestDist = (bestSeed.x >= 0)
        ? abs((int)id.x - bestSeed.x) + abs((int)id.y - bestSeed.y)
        : 0x7FFFFFFF;

    [unroll] for (int dy = -1; dy <= 1; dy++)
        [unroll] for (int dx = -1; dx <= 1; dx++)
        {
            if (dx == 0 && dy == 0) continue;
            int2 neighbor = int2(id.xy) + int2(dx, dy) * stepSize;
            if (neighbor.x < 0 || neighbor.x >= dims.x ||
                neighbor.y < 0 || neighbor.y >= dims.y) continue;

            int2 nSeed = seedMapIn[neighbor];
            if (nSeed.x < 0) continue;

            int nDist = abs((int)id.x - nSeed.x) + abs((int)id.y - nSeed.y);
            if (nDist < bestDist)
            {
                bestDist = nDist;
                bestSeed = nSeed;
            }
        }

    seedMapOut[id.xy] = bestSeed;
    if (bestSeed.x >= 0)
        bgColor[id.xy] = srcTexture[bestSeed];
}
