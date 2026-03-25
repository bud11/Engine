
namespace Engine.GameObjects;


using Engine.Attributes;
using Engine.Core;


using System.Collections.Immutable;
using System.Numerics;



public partial class Skeleton : GameObject
{

    public record class Bone(ushort Index, GameObject Object, Matrix4x4 Rest, Matrix4x4 RestInv);




    public ImmutableDictionary<string, Bone> Bones { get; private set; }
    public ImmutableArray<Bone> BonesByIndex { get; private set; }

    public Bone RootBone { get; private set; }



    [Indexable]
    public GameObject[] BoneObjects
    {
        set
        {
            var arr = new Bone[value.Length];
            var dict = new Dictionary<string, Bone>();

            var thisinv = GlobalTransform;

            for (ushort i = 0; i < value.Length; i++)
            {
                var b = value[i];

                OnGlobalTransformChangedEvent.Add(BoneTransformChanged);

                var inst = new Bone(i, b, b.Transform, thisinv * b.GlobalTransform.Inverse());

                dict[b.Name] = arr[i] = inst;


                if (b.Parent == this)
                {
                    if (RootBone != null) throw new Exception("More than one root bone");

                    RootBone = inst;
                }
            }


            FinalBoneMatrices = new Matrix4x4[value.Length];


            BonesByIndex = ImmutableArray.Create(arr);
            Bones = ImmutableDictionary.ToImmutableDictionary(dict);
        }
    }
    




    private bool NeedsSkeletonRecalc;
    private void BoneTransformChanged() => NeedsSkeletonRecalc = true;



    protected Matrix4x4[] FinalBoneMatrices;

    public void ReuploadSkinningDataIfNeeded()
    {
        if (NeedsSkeletonRecalc)
        {
            var thisinv = GlobalTransform.Inverse();


            for (ushort boneIndex = 0; boneIndex < BonesByIndex.Length; boneIndex++)
            {
                var bone = BonesByIndex[boneIndex];
                FinalBoneMatrices[boneIndex] = bone.RestInv * bone.Object.GlobalTransform * thisinv;
            }



            UploadSkinningData();

            NeedsSkeletonRecalc = false;
        }
    }


    partial void UploadSkinningData();

}
