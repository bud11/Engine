
namespace Engine.Core;

using Engine.GameResources;
using System.Numerics;
using static Engine.Core.EngineMath;
using static Engine.GameResources.ModelResource;



/// <summary>
/// Contains various methods to create meshes, such as primitive shapes.
/// </summary>
public static class MeshGeneration
{


    private static unsafe ModelResource CreateModel(float[] pos, Half[] normals, Half[] uvs, uint[] indices, AABB aabb, string positionBufferName, string normalBufferName, string UVBufferName)
    {

        var buffers = new Dictionary<string, VertexAttributeDefinitionPlusData>()
            {
                {
                    positionBufferName,
                    VertexAttributeDefinitionPlusData.CreateFromArray(
                        new RenderingBackend.VertexAttributeDefinition(RenderingBackend.VertexAttributeBufferComponentFormat.Float,
                                                                sizeof(float)*3,
                                                                0,
                                                                RenderingBackend.VertexAttributeScope.PerVertex), pos, false)
                },

                {
                    normalBufferName,
                    VertexAttributeDefinitionPlusData.CreateFromArray(
                        new RenderingBackend.VertexAttributeDefinition(RenderingBackend.VertexAttributeBufferComponentFormat.Half,
                                                                (ushort)(sizeof(Half)*3),
                                                                0,
                                                                RenderingBackend.VertexAttributeScope.PerVertex), normals, false)
                },

                {
                    UVBufferName,
                    VertexAttributeDefinitionPlusData.CreateFromArray(
                        new RenderingBackend.VertexAttributeDefinition(RenderingBackend.VertexAttributeBufferComponentFormat.Half,
                                                                (ushort)(sizeof(Half)*2),
                                                                0,
                                                                RenderingBackend.VertexAttributeScope.PerVertex), uvs, false)
                },
            };


        var final = VertexAttributeDefinitionPlusData.CreateInterwoven(buffers, true);


        return new ModelResource(
            final,

            [ new SubmeshRange(0, (uint)indices.Length) ],

            indices,
            false,

            aabb,

            null

            );
    }



    /// <summary>
    /// Generates a cube <see cref="ModelResource"/>.
    /// </summary>
    /// <param name="extents"></param>
    /// <param name="positionBufferName"></param>
    /// <param name="normalBufferName"></param>
    /// <param name="UVBufferName"></param>
    /// <returns></returns>
    public static ModelResource GenerateCube(
        Vector3 extents,
        string positionBufferName = "Position",
        string normalBufferName = "Normal",
        string UVBufferName = "UV")
    {
        float x = extents.X;
        float y = extents.Y;
        float z = extents.Z;

        float[] pos = new float[24 * 3];
        Half[] normals = new Half[24 * 3];
        Half[] uvs = new Half[24 * 2];
        uint[] indices = new uint[6 * 2 * 3];

        Vector3[] faceNormals =
        {
            new( 0,  0,  1), // +Z front
            new( 0,  0, -1), // -Z back
            new(-1,  0,  0), // -X left
            new( 1,  0,  0), // +X right
            new( 0,  1,  0), // +Y top
            new( 0, -1,  0), // -Y bottom
        };

        Vector3[,] faceVertices =
        {
            // +Z (front)
            { new(-x,-y, z), new( x,-y, z), new( x, y, z), new(-x, y, z) },
            // -Z (back)
            { new( x,-y,-z), new(-x,-y,-z), new(-x, y,-z), new( x, y,-z) },
            // -X (left)
            { new(-x,-y,-z), new(-x,-y, z), new(-x, y, z), new(-x, y,-z) },
            // +X (right)
            { new( x,-y, z), new( x,-y,-z), new( x, y,-z), new( x, y, z) },
            // +Y (top) — flipped
            { new(-x, y, z), new( x, y, z), new( x, y,-z), new(-x, y,-z) },
            // -Y (bottom) — flipped
            { new(-x,-y, z), new(-x,-y,-z), new( x,-y,-z), new( x,-y, z) }
        };


        Vector2[] faceUVs = { new(0, 0), new(1, 0), new(1, 1), new(0, 1) };

        int vi = 0;
        int ii = 0;

        for (int f = 0; f < 6; f++)
        {
            for (int v = 0; v < 4; v++)
            {
                pos[vi*3 + 0] = faceVertices[f, v].X;
                pos[vi*3 + 1] = faceVertices[f, v].Y;
                pos[vi*3 + 2] = faceVertices[f, v].Z;

                normals[vi*3 + 0] = (Half)faceNormals[f].X;
                normals[vi*3 + 1] = (Half)faceNormals[f].Y;
                normals[vi*3 + 2] = (Half)faceNormals[f].Z;

                uvs[vi*2 + 0] = (Half)faceUVs[v].X;
                uvs[vi*2 + 1] = (Half)faceUVs[v].Y;

                vi++;
            }

            uint baseIndex = (uint)(f * 4);

            indices[ii++] = baseIndex + 0;
            indices[ii++] = baseIndex + 2;
            indices[ii++] = baseIndex + 1;

            indices[ii++] = baseIndex + 0;
            indices[ii++] = baseIndex + 3;
            indices[ii++] = baseIndex + 2;
        }

        return CreateModel(
            pos,
            normals,
            uvs,
            indices,
            AABB.FromMinMax(-extents, extents),
            positionBufferName,
            normalBufferName,
            UVBufferName);
    }



    /// <summary>
    /// Generates a sphere <see cref="ModelResource"/>.
    /// </summary>
    /// <param name="radius"></param>
    /// <param name="rings"></param>
    /// <param name="segments"></param>
    /// <param name="positionBufferName"></param>
    /// <param name="normalBufferName"></param>
    /// <param name="UVBufferName"></param>
    /// <returns></returns>
    public static ModelResource GenerateSphere(float radius, ushort rings, ushort segments, string positionBufferName = "Position", string normalBufferName = "Normal", string UVBufferName = "UV")
    {
        int vertCount = (rings + 1) * (segments + 1);
        int triCount = rings * segments * 2;

        float[] pos = new float[vertCount * 3];
        Half[] normals = new Half[vertCount * 3];
        Half[] uvs = new Half[vertCount * 2];
        uint[] indices = new uint[triCount * 3];

        int vi = 0;
        for (int r = 0; r <= rings; r++)
        {
            float phi = MathF.PI * r / rings;
            float y = MathF.Cos(phi);
            float sinPhi = MathF.Sin(phi);

            for (int s = 0; s <= segments; s++)
            {
                float theta = 2 * MathF.PI * s / segments;
                float x = sinPhi * MathF.Cos(theta);
                float z = sinPhi * MathF.Sin(theta);

                pos[vi*3 + 0] = x * radius;
                pos[vi*3 + 1] = y * radius;
                pos[vi*3 + 2] = z * radius;

                normals[vi*3 + 0] = (Half)x;
                normals[vi*3 + 1] = (Half)y;
                normals[vi*3 + 2] = (Half)z;

                uvs[vi*2 + 0] = (Half)(s / (float)segments);
                uvs[vi*2 + 1] = (Half)(r / (float)rings);

                vi++;
            }
        }

        int ii = 0;
        for (int r = 0; r < rings; r++)
        {
            for (int s = 0; s < segments; s++)
            {
                uint first = (uint)(r * (segments + 1) + s);
                uint second = first + (uint)(segments + 1);

                indices[ii++] = first;
                indices[ii++] = second;
                indices[ii++] = first + 1;

                indices[ii++] = second;
                indices[ii++] = second + 1;
                indices[ii++] = first + 1;
            }
        }

        return CreateModel(pos, normals, uvs, indices, AABB.FromMinMax(new Vector3(-radius), new Vector3(radius)), positionBufferName, normalBufferName, UVBufferName);
    }


    /// <summary>
    /// Generates a capsule <see cref="ModelResource"/>.
    /// </summary>
    /// <param name="radius"></param>
    /// <param name="totalHeight"></param>
    /// <param name="rings"></param>
    /// <param name="segments"></param>
    /// <param name="positionBufferName"></param>
    /// <param name="normalBufferName"></param>
    /// <param name="UVBufferName"></param>
    /// <returns></returns>
    public static ModelResource GenerateCapsule(float radius, float totalHeight, int rings, int segments, string positionBufferName = "Position", string normalBufferName = "Normal", string UVBufferName = "UV")
    {
        int hemiRings = rings;
        int cylRings = 1;
        int vertCount = ((hemiRings+1)* (segments+1)*2) + ((cylRings+1)*(segments+1));
        int triCount = hemiRings*segments*2*2 + cylRings*segments*2;

        float[] pos = new float[vertCount*3];
        Half[] normals = new Half[vertCount*3];
        Half[] uvs = new Half[vertCount*2];
        uint[] indices = new uint[triCount*3];

        int vi = 0;
        int ii = 0;
        float cylinderHeight = totalHeight - 2*radius;

        // Cylinder vertices
        for (int y = 0; y <= cylRings; y++)
        {
            float vy = -cylinderHeight/2 + y*cylinderHeight;
            for (int s = 0; s <= segments; s++)
            {
                float theta = 2*MathF.PI*s/segments;
                float x = radius * MathF.Cos(theta);
                float z = radius * MathF.Sin(theta);

                pos[vi*3 + 0] = x;
                pos[vi*3 + 1] = vy;
                pos[vi*3 + 2] = z;

                Vector3 n = new Vector3(x,0,z).Normalized();
                normals[vi*3 + 0] = (Half)n.X;
                normals[vi*3 + 1] = (Half)n.Y;
                normals[vi*3 + 2] = (Half)n.Z;

                uvs[vi*2 + 0] = (Half)(s/(float)segments);
                uvs[vi*2 + 1] = (Half)(y/(float)cylRings);
                vi++;
            }
        }

        // Cylinder indices
        for (int y = 0; y < cylRings; y++)
        {
            for (int s = 0; s < segments; s++)
            {
                uint baseIdx = (uint)(y*(segments+1) + s);
                uint nextBase = baseIdx + (uint)(segments+1);

                indices[ii++] = baseIdx;
                indices[ii++] = nextBase;
                indices[ii++] = baseIdx+1;

                indices[ii++] = nextBase;
                indices[ii++] = nextBase+1;
                indices[ii++] = baseIdx+1;
            }
        }

        // Top hemisphere
        AddHemisphere(pos, normals, uvs, indices, ref vi, ref ii, radius, cylinderHeight/2, hemiRings, segments, true);

        // Bottom hemisphere
        AddHemisphere(pos, normals, uvs, indices, ref vi, ref ii, radius, -cylinderHeight/2, hemiRings, segments, false);

        return CreateModel(pos, normals, uvs, indices, AABB.FromMinMax(new Vector3(-radius, -totalHeight / 2, -radius), new Vector3(radius, totalHeight / 2, radius)), positionBufferName, normalBufferName, UVBufferName);
    }

    private static void AddHemisphere(float[] pos, Half[] normals, Half[] uvs, uint[] indices, ref int vi, ref int ii, float radius, float yOffset, int rings, int segments, bool top)
    {
        int startIdx = vi;
        for (int r = 0; r <= rings; r++)
        {
            float phi = (MathF.PI/2)*r/rings;
            if(!top) phi = MathF.PI/2 + phi;

            float cosPhi = MathF.Cos(phi);
            float sinPhi = MathF.Sin(phi);

            for (int s = 0; s <= segments; s++)
            {
                float theta = 2*MathF.PI*s/segments;
                float x = sinPhi*MathF.Cos(theta);
                float y = cosPhi;
                float z = sinPhi*MathF.Sin(theta);

                pos[vi*3+0] = x*radius;
                pos[vi*3+1] = y*radius + yOffset;
                pos[vi*3+2] = z*radius;

                normals[vi*3+0] = (Half)x;
                normals[vi*3+1] = (Half)y;
                normals[vi*3+2] = (Half)z;

                uvs[vi*2+0] = (Half)(s/(float)segments);
                uvs[vi*2+1] = (Half)(top? 1 - r/(float)rings : r/(float)rings);
                vi++;
            }
        }

        for (int r = 0; r < rings; r++)
        {
            for (int s = 0; s < segments; s++)
            {
                uint first = (uint)(startIdx + r*(segments+1) + s);
                uint second = first + (uint)(segments+1);

                if(top)
                {
                    indices[ii++] = first;
                    indices[ii++] = second;
                    indices[ii++] = first+1;

                    indices[ii++] = second;
                    indices[ii++] = second+1;
                    indices[ii++] = first+1;
                }
                else
                {
                    indices[ii++] = first;
                    indices[ii++] = first+1;
                    indices[ii++] = second;

                    indices[ii++] = second;
                    indices[ii++] = first+1;
                    indices[ii++] = second+1;
                }
            }
        }
    }

}