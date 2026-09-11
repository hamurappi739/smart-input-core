using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;
using SmartInput.App.ViewModels;

namespace SmartInput.App.Views;

public partial class PredictionPreviewView : UserControl
{
    public PredictionPreviewView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is PredictionPreviewViewModel viewModel)
        {
            viewModel.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName is nameof(PredictionPreviewViewModel.PreviewText)
                    or nameof(PredictionPreviewViewModel.GhostSuffix))
                {
                    UpdateGhostOverlay(viewModel);
                }
            };

            UpdateGhostOverlay(viewModel);
        }
    }

    private void PreviewEditor_OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != TextBox.CaretIndexProperty || DataContext is not PredictionPreviewViewModel viewModel)
        {
            return;
        }

        viewModel.UpdateCaretIndex(PreviewEditor.CaretIndex);
    }

    private void PreviewEditor_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not PredictionPreviewViewModel viewModel)
        {
            return;
        }

        if (e.Key == Key.Tab && viewModel.CanAcceptSuggestion)
        {
            viewModel.AcceptSuggestionCommand.Execute(null);
            PreviewEditor.CaretIndex = viewModel.CaretIndex;
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && viewModel.HasGhostSuggestion)
        {
            viewModel.DismissSuggestionCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void UpdateGhostOverlay(PredictionPreviewViewModel viewModel)
    {
        GhostOverlay.Inlines = new InlineCollection
        {
            new Run(viewModel.PreviewText)
            {
                Foreground = Brushes.Transparent,
            },
            new Run(viewModel.GhostSuffix)
            {
                Foreground = new SolidColorBrush(Color.FromArgb(120, 100, 100, 100)),
                FontStyle = FontStyle.Italic,
            },
        };
    }
}
