



namespace Engine.GameResources;



using System.Numerics;
using static Engine.Core.EngineMath;
using Engine.Core;



#if DEBUG
using System.Text.Json;
using static Engine.Core.Parsing;
#endif



[FileExtensionAssociation(".anim")]
public class AnimationResource : GameResource, GameResource.ILoads,

#if DEBUG
    GameResource.IConverts
#endif
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


    public static async Task<byte[]> ConvertToFinalAssetBytes(Loading.Bytes bytes, string filePath)
    {

        var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(bytes.ByteArray, Parsing.JsonAssetLoadingOptions);

        bytes.Dispose();



        var write = ValueWriter.CreateWithBufferWriter();


        // header
        write.WriteUnmanaged(dict["length"].GetSingle());
        write.WriteUnmanaged(dict["loop"].GetBoolean());


        var tracks = dict["tracks"];
        write.WriteUnmanaged((uint)tracks.GetArrayLength());


        foreach (var track in tracks.EnumerateArray())
        {
            var type = Enum.Parse<TrackTypes>(track.GetProperty("type").GetString(), true);
            var keys = track.GetProperty("keys");


            write.WriteString(track.GetProperty("identifier").GetString());
            write.WriteUnmanaged((byte)type);
            write.WriteUnmanaged((uint)keys.GetArrayLength());


            // times
            foreach (var key in keys.EnumerateArray())
                write.WriteUnmanaged(key.GetProperty("time").GetSingle());

            // values
            foreach (var key in keys.EnumerateArray())
            {
                var value = key.GetProperty("value");

                switch (type)
                {
                    case TrackTypes.Position:
                    case TrackTypes.Scale:
                        var arr = value.EnumerateArray();
                        write.WriteUnmanaged(arr.ElementAt(0).GetSingle());
                        write.WriteUnmanaged(arr.ElementAt(1).GetSingle());
                        write.WriteUnmanaged(arr.ElementAt(2).GetSingle());
                        break;

                    case TrackTypes.Rotation:
                        var arr2 = value.EnumerateArray();
                        write.WriteUnmanaged(arr2.ElementAt(0).GetSingle());
                        write.WriteUnmanaged(arr2.ElementAt(1).GetSingle());
                        write.WriteUnmanaged(arr2.ElementAt(2).GetSingle());
                        write.WriteUnmanaged(arr2.ElementAt(3).GetSingle());
                        break;

                    case TrackTypes.Value:
                        write.WriteUnmanaged(value.GetSingle());
                        break;
                }
            }
        }

        return write.GetSpan().ToArray();

    }


#endif





    public static async Task<GameResource> Load(Loading.AssetByteStream stream, string key)
    {

        var read = ValueReader.FromStream(stream);



        var length = read.ReadUnmanaged<float>();
        var loops = read.ReadUnmanaged<bool>();


        var trackcount = read.ReadUnmanaged<uint>();



        TrackData[] tracks = new TrackData[trackcount];


        for (uint tr = 0; tr < trackcount; tr++)
        {
            var identifier = read.ReadString();


            var tracktype = (TrackTypes)read.ReadUnmanaged<byte>();


            var timesarray = read.ReadLengthPrefixedUnmanagedSpan<float>();

            object contentarray = null;

            switch (tracktype)
            {
                case TrackTypes.Position:
                case TrackTypes.Scale:

                    contentarray = read.ReadLengthPrefixedUnmanagedSpan<Vector3>();

                    break;

                case TrackTypes.Rotation:

                    contentarray = read.ReadLengthPrefixedUnmanagedSpan<Quaternion>();

                    break;

                case TrackTypes.Value:

                    contentarray = read.ReadLengthPrefixedUnmanagedSpan<float>();

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
