using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using SmartInput.App.Navigation;
using SmartInput.App.Services;

namespace SmartInput.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IApplicationThemeService _themeService;
    private bool _isSynchronizingTheme;

    public MainWindowViewModel(
        HomeViewModel homeViewModel,
        MyDictionaryViewModel myDictionaryViewModel,
        SnippetsViewModel snippetsViewModel,
        HotkeysViewModel hotkeysViewModel,
        PrivacyViewModel privacyViewModel,
        AdditionalViewModel additionalViewModel,
        IApplicationThemeService themeService)
    {
        _themeService = themeService;
        _themeService.ThemeChanged += OnThemeChanged;

        NavigationItems =
        [
            new NavigationItem("Главная", homeViewModel),
            new NavigationItem("Мой словарь", myDictionaryViewModel),
            new NavigationItem("Шаблоны текста", snippetsViewModel),
            new NavigationItem("Горячие клавиши", hotkeysViewModel),
            new NavigationItem("Дополнительно", additionalViewModel),
            new NavigationItem("Конфиденциальность", privacyViewModel),
        ];

        SelectedNavigationItem = NavigationItems[0];
        CurrentPage = SelectedNavigationItem.Page;

        _isSynchronizingTheme = true;
        IsDarkTheme = _themeService.IsDarkTheme;
        _isSynchronizingTheme = false;
    }

    public ObservableCollection<NavigationItem> NavigationItems { get; }

    [ObservableProperty]
    private NavigationItem? _selectedNavigationItem;

    [ObservableProperty]
    private ViewModelBase _currentPage;

    [ObservableProperty]
    private bool _isDarkTheme;

    public string ThemeDescription => IsDarkTheme
        ? "Тёмная тема включена"
        : "Светлая тема включена";

    partial void OnSelectedNavigationItemChanged(NavigationItem? value)
    {
        if (value is not null)
        {
            CurrentPage = value.Page;
        }
    }

    partial void OnIsDarkThemeChanged(bool value)
    {
        OnPropertyChanged(nameof(ThemeDescription));

        if (!_isSynchronizingTheme)
        {
            _ = _themeService.SetDarkThemeAsync(value);
        }
    }

    private void OnThemeChanged()
    {
        void Sync()
        {
            _isSynchronizingTheme = true;
            IsDarkTheme = _themeService.IsDarkTheme;
            _isSynchronizingTheme = false;
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            Sync();
            return;
        }

        Dispatcher.UIThread.Post(Sync);
    }
}
