


using Engine.Core;
using Engine.GameResources;

namespace Engine.GameObjects;




/// <summary>
/// A <see cref="Core.GameObject"/> that issues draw calls.
/// </summary>
public abstract partial class DrawObject : AABBObject
{





    public static readonly List<DrawObject> AllDrawableObjects = new();


    public bool DrawnThisFrame { get; private set; }



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


    public unsafe abstract void Draw(delegate*<MaterialResource, MaterialResource.MaterialResolution> resolver);



    public readonly ThreadSafeEventAction OnPreDraw = new();


}