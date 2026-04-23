

using Engine.Core;
using Engine.GameResources;

namespace Engine.GameObjects;

using static Engine.Core.References;


#if DEBUG
using Engine.Stripped;
#endif



/// <summary>
/// A <see cref="Core.GameObject"/> that issues draw calls.
/// </summary>
public abstract partial class DrawObject : AABBObject
{

    




    public static readonly List<DrawObject> AllDrawObjects = new();


    private bool DrawnThisFrame;



    public override void Loop()
    {
        DrawnThisFrame = false;

        base.Loop();
    }



    /// <summary>
    /// Called before calls to <see cref="Draw"/>, but no more than once per frame.
    /// <br /> In other words, even if <see cref="Draw"/> is called multiple times in a frame, this will only be called right before the first time.
    /// </summary>
    public unsafe virtual void PreDraw()
    {
        OnPreDraw.Invoke();

        DrawnThisFrame = true;
    }




    /// <summary>
    /// Contains draw-call-issuer-supplied data needed to issue structured draw calls via <see cref="Draw(DrawState)"/>.
    /// </summary>
    public unsafe struct DrawState
    {
        public delegate*<MaterialResource, MaterialResource.MaterialResolution> MaterialResolver;
        public UnmanagedKeyValueCollection<WeakObjRef<string>, WeakObjRef<RenderingBackend.BackendResourceSetReference>> TransientResourceSets;
    }


    public unsafe virtual void Draw(DrawState state)
    {
        if (!DrawnThisFrame) 
            PreDraw();
    }




    public readonly ThreadSafeEventAction OnPreDraw = new();


}