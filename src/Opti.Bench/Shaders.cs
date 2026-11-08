using System.Globalization;
using System.Text;

namespace Opti.Bench;

internal static class Shaders
{
    /// <summary>
    /// Куб генерируется в вершинном шейдере по SV_VertexID (без вершинных буферов), позиции экземпляров — по SV_InstanceID.
    /// Пиксельный шейдер с настраиваемым циклом даёт управляемую нагрузку на GPU.
    /// </summary>
    public static string Source()
    {
        // 6 граней × 2 треугольника × 3 вершины
        var faces = new (float[] n, float[][] quad)[]
        {
            (new[] { 0f, 0, 1 }, new[] { new[] { -1f, -1, 1 }, new[] { 1f, -1, 1 }, new[] { 1f, 1, 1 }, new[] { -1f, 1, 1 } }),
            (new[] { 0f, 0, -1 }, new[] { new[] { 1f, -1, -1 }, new[] { -1f, -1, -1 }, new[] { -1f, 1, -1 }, new[] { 1f, 1, -1 } }),
            (new[] { 1f, 0, 0 }, new[] { new[] { 1f, -1, 1 }, new[] { 1f, -1, -1 }, new[] { 1f, 1, -1 }, new[] { 1f, 1, 1 } }),
            (new[] { -1f, 0, 0 }, new[] { new[] { -1f, -1, -1 }, new[] { -1f, -1, 1 }, new[] { -1f, 1, 1 }, new[] { -1f, 1, -1 } }),
            (new[] { 0f, 1, 0 }, new[] { new[] { -1f, 1, 1 }, new[] { 1f, 1, 1 }, new[] { 1f, 1, -1 }, new[] { -1f, 1, -1 } }),
            (new[] { 0f, -1, 0 }, new[] { new[] { -1f, -1, -1 }, new[] { 1f, -1, -1 }, new[] { 1f, -1, 1 }, new[] { -1f, -1, 1 } }),
        };
        string V(float[] v) => string.Format(CultureInfo.InvariantCulture, "float3({0},{1},{2})", v[0], v[1], v[2]);
        var pos = new StringBuilder();
        var nrm = new StringBuilder();
        foreach (var (n, q) in faces)
        {
            foreach (var i in new[] { 0, 1, 2, 0, 2, 3 }) pos.Append(V(q[i])).Append(',');
            nrm.Append(V(n)).Append(',');
        }
        return $$"""
            cbuffer Frame : register(b0)
            {
                row_major float4x4 viewProj;
                float time;
                float load;
                uint gridX;
                uint gridY;
            };
            static const float3 kPos[36] = { {{pos.ToString().TrimEnd(',')}} };
            static const float3 kNrm[6] = { {{nrm.ToString().TrimEnd(',')}} };

            struct VSOut { float4 pos : SV_Position; float3 nrm : NORMAL; float3 col : COLOR; float3 world : WORLDPOS; };

            VSOut VSMain(uint vid : SV_VertexID, uint iid : SV_InstanceID)
            {
                float3 p = kPos[vid];
                float3 n = kNrm[vid / 6];
                uint gx = iid % gridX;
                uint gy = (iid / gridX) % gridY;
                uint gz = iid / (gridX * gridY);
                float3 offset = (float3(gx, gy, gz) - float3(gridX - 1, gridY - 1, 0) * 0.5) * 3.2;
                float a = time * (0.6 + frac(iid * 0.618034) * 1.4) + iid;
                float s = sin(a), c = cos(a);
                float3x3 ry = float3x3(c, 0, -s, 0, 1, 0, s, 0, c);
                float3x3 rx = float3x3(1, 0, 0, 0, c, s, 0, -s, c);
                float3 rp = mul(mul(p, ry), rx) * 0.9;
                VSOut o;
                o.world = rp + offset;
                o.pos = mul(float4(o.world, 1), viewProj);
                o.nrm = mul(mul(n, ry), rx);
                o.col = 0.5 + 0.5 * cos(6.2831 * (frac(iid * 0.0137) + float3(0.0, 0.33, 0.67)));
                return o;
            }

            float4 PSMain(VSOut i) : SV_Target
            {
                float3 n = normalize(i.nrm);
                float d = saturate(dot(n, normalize(float3(0.4, 0.8, -0.45))));
                float acc = 0;
                [loop] for (int k = 0; k < (int)load; k++)
                    acc += sin(i.world.x * 0.37 + k) * cos(i.world.y * 0.21 - k * 0.5);
                float3 col = i.col * (0.2 + 0.8 * d) + acc * 1e-5;
                return float4(col, 1);
            }
            """;
    }
}
