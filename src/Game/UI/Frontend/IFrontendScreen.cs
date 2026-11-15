namespace Usa.Game.UI.Frontend;

/// <summary>A front-end scene root the shell instantiates; it builds itself from the context once it is in the tree.</summary>
public interface IFrontendScreen
{
    void Initialize(FrontendContext context);
}
