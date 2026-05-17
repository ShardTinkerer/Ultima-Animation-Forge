using Avalonia.Controls;
using Avalonia.Interactivity;

namespace UltimaAnimationForge.Views;

public partial class TileDataFlagEditorWindow : Window
{
    public TileDataFlagEditorWindow()
    {
        InitializeComponent();
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}