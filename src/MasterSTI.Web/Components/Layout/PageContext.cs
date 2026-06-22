namespace MasterSTI.Web.Components.Layout;

/// <summary>
/// Per-circuit state holder so a page can set the topbar's title + breadcrumb
/// from inside its own component tree (cascading values only flow downward, so
/// the layout subscribes to this state instead).
/// </summary>
public sealed class PageContext
{
    public string Title { get; private set; } = "Panou principal";
    public string Breadcrumb { get; private set; } = "VeraSign";

    public event Action? OnChange;

    public void Set(string title, string breadcrumb)
    {
        if (title == Title && breadcrumb == Breadcrumb)
        {
            return;
        }
        Title = title;
        Breadcrumb = breadcrumb;
        OnChange?.Invoke();
    }
}
