


namespace Engine.GameResources;

using System.Numerics;


using Engine.Attributes;
using static Engine.Core.EngineMath;
using Engine.Core;



#if DEBUG
using System.Text.Json;
#endif




[FileExtensionAssociation(".col")]
public class CollisionMeshResource : GameResource
{

    public readonly AABB BaseAABB;



    
    public static new async Task<GameResource> Load(Loading.AssetByteStream stream, string key)
    {

        uint tricount = stream.ReadUnmanagedType<uint>();


        CollisionMeshTriangle[] tris = new CollisionMeshTriangle[tricount];

        for (uint i = 0; i < tricount; i++)
        {
            tris[i] = new CollisionMeshTriangle()
            {
                position1 = stream.ReadUnmanagedType<Vector3>(),
                position2 = stream.ReadUnmanagedType<Vector3>(),
                position3 = stream.ReadUnmanagedType<Vector3>(),

                normal = stream.ReadUnmanagedType<Vector3>(),

                meta = (byte)stream.ReadByte()
            };
        }

        Vector3 min = stream.ReadUnmanagedType<Vector3>();
        Vector3 max = stream.ReadUnmanagedType<Vector3>();

        var aabb = AABB.FromMinMax(min, max);


        return new CollisionMeshResource(tris, aabb, key);
    }



#if DEBUG


    
    public static new async Task<byte[]> ConvertToFinalAssetBytes(byte[] bytes, string filePath)
    {
        var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(bytes);


        List<byte> final = new();



        // AABB
        if (dict.TryGetValue("localAABB", out var aabb))
        {
            var min = JsonSerializer.Deserialize<float[]>(aabb.GetProperty("min"));
            var max = JsonSerializer.Deserialize<float[]>(aabb.GetProperty("max"));

            foreach (var f in min) final.AddRange(BitConverter.GetBytes(f));
            foreach (var f in max) final.AddRange(BitConverter.GetBytes(f));
        }
        else
        {
            for (int i = 0; i < 6; i++)
                final.AddRange(BitConverter.GetBytes(float.MaxValue));
        }

        return final.ToArray();
    }



#endif




    /// <summary>
    /// A basic triangle. <paramref name="meta"/> can be set to inform arbitrary per-triangle data, such as for example surface type.
    /// </summary>
    /// <param name="position1"></param>
    /// <param name="position2"></param>
    /// <param name="position3"></param>
    /// <param name="normal"></param>
    /// <param name="meta"></param>
    public readonly record struct CollisionMeshTriangle(

        Vector3 position1, 
        Vector3 position2, 
        Vector3 position3, 

        Vector3 normal, 

        byte meta
    
        );



    private readonly CollisionMeshTriangle[] Triangles;

    public CollisionMeshResource(

        CollisionMeshTriangle[] triangles,

        AABB baseAABB = default,

        string key = null) : base(key)
    {

        Triangles = triangles;
        BaseAABB = baseAABB;
    }



}
