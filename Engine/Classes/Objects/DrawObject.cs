


using Engine.GameResources;

namespace Engine.GameObjects;




/// <summary>
/// A <see cref="GameObject"/> that issues draw calls.
/// </summary>
public abstract class DrawObject : AABBObject
{

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
        DrawnThisFrame = true;
    }


    public unsafe abstract void Draw(delegate*<DrawObject, MaterialResource, MaterialResource.MaterialDefinition> MaterialDefinitionMutator = null);



}