using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.ObjectModel;
using System.Reflection.Metadata;

namespace UltimaAnimationForge.Models;

public sealed partial class AnimDataEntry : ObservableObject
{
    [ObservableProperty]
    private bool isChecked;

    [ObservableProperty]
    private int id;

    [ObservableProperty]
    private string tileName = string.Empty;

    [ObservableProperty]
    private byte unknown;

    [ObservableProperty]
    private byte frameCount;

    [ObservableProperty]
    private byte frameInterval;

    [ObservableProperty]
    private byte frameStart;

    [ObservableProperty]
    private bool artExists;

    [ObservableProperty]
    private bool hasAnimationTileFlag;

    public sbyte[] FrameOffsets { get; set; } = new sbyte[64];

    public ObservableCollection<AnimDataFrameEntry> Frames { get; } = new();

    public string DisplayText =>
        string.IsNullOrWhiteSpace(TileName)
            ? "0x" + Id.ToString("X4")
            : "0x" + Id.ToString("X4") + " " + TileName;

    public string SecondaryText =>
        "Frames: " + FrameCount +
        " | Interval: " + FrameInterval +
        " | Start: " + FrameStart +
        " | " + ValidationText;

    public string ValidationText
    {
        get
        {
            if (!ArtExists)
            {
                return "Missing base art";
            }

            if (!HasAnimationTileFlag)
            {
                return "No TileData animation flag";
            }

            return "OK";
        }
    }

    partial void OnTileNameChanged(string value)
    {
        OnPropertyChanged(nameof(DisplayText));
    }

    partial void OnFrameCountChanged(byte value)
    {
        if (value > 64)
        {
            FrameCount = 64;
            return;
        }

        OnPropertyChanged(nameof(SecondaryText));
    }

    partial void OnFrameIntervalChanged(byte value)
    {
        OnPropertyChanged(nameof(SecondaryText));
    }

    partial void OnFrameStartChanged(byte value)
    {
        OnPropertyChanged(nameof(SecondaryText));
    }

    partial void OnArtExistsChanged(bool value)
    {
        OnPropertyChanged(nameof(SecondaryText));
        OnPropertyChanged(nameof(ValidationText));
    }

    partial void OnHasAnimationTileFlagChanged(bool value)
    {
        OnPropertyChanged(nameof(SecondaryText));
        OnPropertyChanged(nameof(ValidationText));
    }

    public void RebuildFrames(
        Func<int, string> nameResolver,
        Func<int, bool> artExistsResolver,
        Func<int, WriteableBitmap?> bitmapResolver)
    {
        Frames.Clear();

        int safeCount = Math.Clamp(FrameCount, (byte)0, (byte)64);

        for (int index = 0; index < safeCount; index++)
        {
            int graphicId = Id + FrameOffsets[index];

            Frames.Add(new AnimDataFrameEntry
            {
                FrameIndex = index,
                Offset = FrameOffsets[index],
                GraphicId = graphicId,
                TileName = nameResolver(graphicId),
                ArtExists = artExistsResolver(graphicId),
                Bitmap = bitmapResolver(graphicId)
            });
        }
    }
}

public sealed partial class AnimDataFrameEntry : ObservableObject
{
    [ObservableProperty]
    private WriteableBitmap? bitmap;

    [ObservableProperty]
    private int frameIndex;

    [ObservableProperty]
    private sbyte offset;

    [ObservableProperty]
    private int graphicId;

    [ObservableProperty]
    private string tileName = string.Empty;

    [ObservableProperty]
    private bool artExists;

    public string DisplayText =>
        (FrameIndex + 1).ToString("D2") +
        " | 0x" + GraphicId.ToString("X4") +
        " | Offset " + Offset +
        (string.IsNullOrWhiteSpace(TileName) ? string.Empty : " | " + TileName);

    public string StatusText => ArtExists ? "OK" : "Missing art";
}
