


namespace Engine.GameResources;

using System.Numerics;


using Engine.Attributes;
using static Engine.Core.EngineMath;
using Engine.Core;



#if DEBUG
using System.Text.Json;
using static Engine.Core.Parsing;
#endif




[FileExtensionAssociation(".col")]
public class CollisionMeshResource : GameResource, GameResource.ILoads,

#if DEBUG
    GameResource.IConverts
#endif
{

    public readonly AABB BaseAABB;



    
    public static async Task<GameResource> Load(Loading.AssetByteStream stream, string key)
    {
        var reader = ValueReader.FromStream(stream);

        uint tricount = reader.ReadUnmanaged<uint>();


        CollisionMeshTriangle[] tris = new CollisionMeshTriangle[tricount];

        for (uint i = 0; i < tricount; i++)
        {
            tris[i] = new CollisionMeshTriangle()
            {
                position1 = reader.ReadUnmanaged<Vector3>(),
                position2 = reader.ReadUnmanaged<Vector3>(),
                position3 = reader.ReadUnmanaged<Vector3>(),

                normal = reader.ReadUnmanaged<Vector3>(),

                meta = (byte)stream.ReadByte()
            };
        }

        Vector3 min = reader.ReadUnmanaged<Vector3>();
        Vector3 max = reader.ReadUnmanaged<Vector3>();

        var aabb = AABB.FromMinMax(min, max);


        return new CollisionMeshResource(tris, aabb, key);
    }



#if DEBUG


    public static async Task<byte[]> ConvertToFinalAssetBytes(Loading.Bytes bytes, string filePath)
    {
        var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(bytes.ByteArray, Parsing.JsonAssetLoadingOptions);

        bytes.Dispose();


        var write = ValueWriter.CreateWithBufferWriter();



        // AABB
        if (dict.TryGetValue("LocalAABB", out var aabb))
        {
            var min = JsonSerializer.Deserialize<float[]>(aabb.GetProperty("Min"), Parsing.JsonAssetLoadingOptions);
            var max = JsonSerializer.Deserialize<float[]>(aabb.GetProperty("Max"), Parsing.JsonAssetLoadingOptions);

            foreach (var f in min) write.WriteUnmanaged(f);
            foreach (var f in max) write.WriteUnmanaged(f);
        }
        else
        {
            for (int i = 0; i < 6; i++)
                write.WriteUnmanaged(float.MaxValue);
        }


        return write.GetSpan().ToArray();
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
