using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace UltimaAnimationForge.Models;

public partial class TileDataEntry : ObservableObject
{
    [ObservableProperty]
    private bool isEdited;

    public IBrush DisplayBrush => IsEdited
        ? Brushes.Orange
        : Brushes.White;

    partial void OnIsEditedChanged(bool value)
    {
        OnPropertyChanged(nameof(DisplayBrush));
    }
    public bool IsLand { get; set; }
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public ulong Flags { get; set; }

    public ushort TextureId { get; set; }

    public short Animation { get; set; }
    public byte Weight { get; set; }
    public byte Quality { get; set; }
    public byte Quantity { get; set; }
    public byte Hue { get; set; }
    public byte Height { get; set; }
    public byte StackingOffset { get; set; }
    public byte Value { get; set; }
    public ushort MiscData { get; set; }
    public byte Unknown2 { get; set; }
    public byte Unknown3 { get; set; }

    public string TypeText => IsLand ? "Land" : "Item";
    public string IdText => "0x" + Id.ToString("X4");
    public string DisplayText => IdText + " | " + TypeText + " | " + Name;
}

public partial class TileDataFlagOption : ObservableObject
{
    public int BitIndex { get; set; }
    public ulong Mask => 1UL << BitIndex;

    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    [ObservableProperty]
    private bool isChecked;
}