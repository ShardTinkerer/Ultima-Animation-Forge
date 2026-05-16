using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace UltimaAnimationForge.Models;

public partial class ArtEntry : ObservableObject
{
    [ObservableProperty]
    private bool isChecked;
    public bool IsFreeSlot { get; set; }
    public int ArtId { get; set; }
    public int FileIndex { get; set; }
    public string Type { get; set; } = string.Empty;
    public string SecondaryText { get; set; } = string.Empty;
    public WriteableBitmap? Thumbnail { get; set; }

    public string DisplayText => Type + " 0x" + ArtId.ToString("X4") + " (" + ArtId + ")";
    public string ExportFileName => Type.ToLowerInvariant() + "_0x" + ArtId.ToString("X4") + ".png";
}