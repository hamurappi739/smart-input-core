namespace SmartInput.App.Navigation;

public sealed class NavigationItem
{
    public NavigationItem(string title, ViewModels.ViewModelBase page)
    {
        Title = title;
        Page = page;
    }

    public string Title { get; }

    public ViewModels.ViewModelBase Page { get; }
}
