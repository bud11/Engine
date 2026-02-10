
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



    
    public new void Init(GameObject[] Bones, string Name = default, Matrix4x4 Transform = default)
    {

        var arr = new Bone[Bones.Length];
        var dict = new Dictionary<string, Bone>();

        var thisinv = GlobalTransform.Matrix;

        for (ushort i = 0; i < Bones.Length; i++)
        {
            var b = Bones[i];

            OnGlobalTransformChangedEvent.Add(BoneTransformChanged);

            var inst = new Bone(i, b, b.Transform.Matrix, thisinv * b.GlobalTransform.AffineInverse().Matrix);

            dict[b.Name] = arr[i] = inst;


            if (b.Parent == this)
            {
                if (RootBone != null) throw new Exception("More than one root bone");

                RootBone = inst;
            }
        }


        FinalBoneMatrices = new Matrix4x4[Bones.Length];


        BonesByIndex = ImmutableArray.Create(arr);
        this.Bones = ImmutableDictionary.ToImmutableDictionary(dict);

        base.Init(Name, Transform);
    }



    private bool NeedsSkeletonRecalc;
    private void BoneTransformChanged() => NeedsSkeletonRecalc = true;



    protected Matrix4x4[] FinalBoneMatrices;

    public void RecalculateAndUploadSkinningDataIfNeeded()
    {
        if (NeedsSkeletonRecalc)
        {
            var thisinv = GlobalTransform.AffineInverse().Matrix;


            for (ushort boneIndex = 0; boneIndex < BonesByIndex.Length; boneIndex++)
            {
                var bone = BonesByIndex[boneIndex];
                FinalBoneMatrices[boneIndex] = bone.RestInv * bone.Object.GlobalTransform.Matrix * thisinv;
            }



            UploadSkinningData();

            NeedsSkeletonRecalc = false;
        }
    }


    partial void UploadSkinningData();

}
