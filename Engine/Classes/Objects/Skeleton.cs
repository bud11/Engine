
namespace Engine.GameObjects;


using Engine.Attributes;
using System.Collections.Immutable;
using System.Numerics;



public partial class Skeleton : GameObject
{

    public class Bone
    {
        public byte Index;

        public GameObject Object;
        public Matrix4x4 Rest;
        public Matrix4x4 RestInv;
    }



    public ImmutableDictionary<string, Bone> Bones { get; private set; }
    public ImmutableArray<Bone> BonesByIndex { get; private set; }

    public Bone RootBone { get; private set; }



    [GameObjectInitMethod]
    public new void Init(GameObject[] Bones)
    {

        var arr = new Bone[Bones.Length];
        var dict = new Dictionary<string, Bone>();

        var thisinv = GlobalTransform.Matrix;

        for (byte i = 0; i < Bones.Length; i++)
        {
            var b = Bones[i];

            OnGlobalTransformChangedEvent.Add(BoneTransformChanged);

            var inst = new Bone() { Object = b, Rest = b.Transform.Matrix, RestInv = thisinv * b.GlobalTransform.AffineInverse().Matrix };
            inst.Index = i;

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

        Init();
    }


    private bool NeedsSkeletonRecalc;
    private void BoneTransformChanged() => NeedsSkeletonRecalc = true;



    private Matrix4x4[] FinalBoneMatrices;

    public void RecalculateAndUploadSkinningDataIfNeeded()
    {
        if (NeedsSkeletonRecalc)
        {
            var thisinv = GlobalTransform.AffineInverse().Matrix;

            for (int bone = 0; bone < BonesByIndex.Length; bone++)
                FinalBoneMatrices[bone] = BonesByIndex[bone].RestInv * BonesByIndex[bone].Object.GlobalTransform.Matrix * thisinv;


            UploadSkinningData();

            NeedsSkeletonRecalc = false;
        }
    }


    partial void UploadSkinningData();

}
