



namespace Engine.GameResources;



using System.Numerics;


using Engine.Attributes;
using static Engine.Core.EngineMath;
using Engine.Core;



#if DEBUG
using System.Text.Json;
#endif



[FileExtensionAssociation(".anim")]
public class AnimationResource : GameResource
{


    public enum TrackTypes
    {
        Position,
        Rotation,
        Scale,
        Value
    }

    public readonly record struct TrackData(string Identifier, TrackTypes Type, float[] Times, object Data);



    public bool Loops;
    public float Length;
    public TrackData[] Tracks;


    public AnimationResource(float length, bool loops, TrackData[] tracks, string key) : base(key)
    {
        Tracks = tracks;
        Length = length;
        Loops = loops;
    }



#if DEBUG


    
    public static new async Task<byte[]> ConvertToFinalAssetBytes(byte[] bytes, string filePath)
    {

        var s = new JsonSerializerOptions() { PropertyNameCaseInsensitive = true };

        var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(bytes, s);

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        // header
        bw.Write(dict["length"].GetSingle());
        bw.Write(dict["loop"].GetBoolean());


        var tracks = dict["tracks"];
        bw.Write((uint)tracks.GetArrayLength());


        foreach (var track in tracks.EnumerateArray())
        {
            var id = track.GetProperty("identifier").GetString();
            var type = Parsing.EnumParse<TrackTypes>(track.GetProperty("type").GetString());
            var keys = track.GetProperty("keys");


            bw.Write(Parsing.GetUintLengthPrefixedUTF8StringAsBytes(id));
            bw.Write((byte)type);
            bw.Write((uint)keys.GetArrayLength());

            // times
            foreach (var key in keys.EnumerateArray())
                bw.Write(key.GetProperty("time").GetSingle());

            // values
            foreach (var key in keys.EnumerateArray())
            {
                var value = key.GetProperty("value");

                switch (type)
                {
                    case TrackTypes.Position:
                    case TrackTypes.Scale:
                        var arr = value.EnumerateArray();
                        bw.Write(arr.ElementAt(0).GetSingle());
                        bw.Write(arr.ElementAt(1).GetSingle());
                        bw.Write(arr.ElementAt(2).GetSingle());
                        break;

                    case TrackTypes.Rotation:
                        var arr2 = value.EnumerateArray();
                        bw.Write(arr2.ElementAt(0).GetSingle());
                        bw.Write(arr2.ElementAt(1).GetSingle());
                        bw.Write(arr2.ElementAt(2).GetSingle());
                        bw.Write(arr2.ElementAt(3).GetSingle());
                        break;

                    case TrackTypes.Value:
                        bw.Write(value.GetSingle());
                        break;
                }
            }
        }

        return ms.ToArray();

    }


#endif





    
    public static new async Task<GameResource> Load(Loading.AssetByteStream stream, string key)
    {
        var length = stream.ReadUnmanagedType<float>();
        var loops = stream.ReadByte() == 1;


        var trackcount = stream.ReadUnmanagedType<uint>();



        TrackData[] tracks = new TrackData[trackcount];


        for (uint tr = 0; tr < trackcount; tr++)
        {
            var identifier = stream.ReadUintLengthPrefixedUTF8String();


            var tracktype = (TrackTypes)stream.ReadByte();

            var trackvaluescount = stream.ReadUnmanagedType<uint>();


            var timesarray = stream.ReadUnmanagedTypeArray<float>(trackvaluescount);

            object contentarray = null;

            switch (tracktype)
            {
                case TrackTypes.Position:
                case TrackTypes.Scale:

                    contentarray = stream.ReadUnmanagedTypeArray<Vector3>(trackvaluescount);

                    break;

                case TrackTypes.Rotation:

                    contentarray = stream.ReadUnmanagedTypeArray<Quaternion>(trackvaluescount);

                    break;

                case TrackTypes.Value:

                    contentarray = stream.ReadUnmanagedTypeArray<float>(trackvaluescount);

                    break;

            }


            tracks[tr] = new()
            {
                Data = contentarray,
                Identifier = identifier,
                Type = tracktype,
                Times = timesarray,
            };
        }


        return new AnimationResource(length, loops, tracks, key);
    }











    public Vector3 SampleVec3TrackData(int track, float time) => SampleVec3TrackData(Tracks[track], time);
    public Quaternion SampleQuatTrackData(int track, float time) => SampleQuatTrackData(Tracks[track], time);
    public float SampleValueTrackData(int track, float time) => SampleValueTrackData(Tracks[track], time);






    public static Vector3 SampleVec3TrackData(TrackData track, float time)
    {
        Vector3[] src = (Vector3[])track.Data;
        float[] times = track.Times;

        int kframe = FloorToInt(time * 60);

        if (kframe > src.Length - 1) return src[^1];


        float time1 = times[kframe];
        float time2 = times[int.Min(kframe + 1, src.Length - 1)];

        float t = EngineMath.InverseLerp(time1, time2, time);

        return Vector3.Lerp(src[kframe], src[int.Min(kframe + 1, src.Length - 1)], t);

    }


    public static Quaternion SampleQuatTrackData(TrackData track, float time)
    {
        Quaternion[] src = (Quaternion[])track.Data;
        float[] times = track.Times;

        int kframe = FloorToInt(time * 60);

        if (kframe > src.Length - 1) return src[^1];


        float time1 = times[kframe];
        float time2 = times[int.Min(kframe + 1, src.Length - 1)];

        float t = EngineMath.InverseLerp(time1, time2, time);

        return Quaternion.Lerp(src[kframe], src[int.Min(kframe + 1, src.Length - 1)], t);

    }


    public static float SampleValueTrackData(TrackData track, float time)
    {
        float[] src = (float[])track.Data;
        float[] times = track.Times;

        int kframe = FloorToInt(time * 60);

        if (kframe > src.Length - 1) return src[^1];


        float time1 = times[kframe];
        float time2 = times[int.Min(kframe + 1, src.Length - 1)];

        float t = EngineMath.InverseLerp(time1, time2, time);

        return float.Lerp(src[kframe], src[int.Min(kframe + 1, src.Length - 1)], t);

    }


    protected override void OnFree() { }
}
