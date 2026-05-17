// ViewModels/MainWindowViewModel.TileData.cs

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Input;
using UltimaAnimationForge.Models;
using UltimaAnimationForge.Services;

namespace UltimaAnimationForge.ViewModels;

public partial class MainWindowViewModel
{
    public ObservableCollection<TileDataEntry> TileDataEntries { get; } = new();
    public ObservableCollection<TileDataEntry> FilteredTileDataEntries { get; } = new();
    public ObservableCollection<TileDataFlagOption> SelectedTileDataFlagOptions { get; } = new();
    private readonly TileDataMulService tileDataMulService = new();
    private static readonly string[] TileDataFlagNames =
{
    "Background", "Weapon", "Transparent", "Translucent", "Wall", "Damaging", "Impassable", "Wet",
    "Unknown1", "Surface", "Bridge", "Generic", "Window", "NoShoot", "ArticleA", "ArticleAn",
    "ArticleThe", "Foliage", "PartialHue", "NoHouse", "Map", "Container", "Wearable", "LightSource",
    "Animation", "HoverOver", "NoDiagonal", "Armor", "Roof", "Door", "StairBack", "StairRight",
    "AlphaBlend", "UseNewArt", "ArtUsed", "Unused8", "NoShadow", "PixelBleed", "PlayAnimOnce",
    "MultiMovable", "Unused10", "Unused11", "Unused12", "Unused13", "Unused14", "Unused15",
    "Unused16", "Unused17", "Unused18", "Unused19", "Unused20", "Unused21", "Unused22", "Unused23",
    "Unused24", "Unused25", "Unused26", "Unused27", "Unused28", "Unused29", "Unused30", "Unused31",
    "Unused32"
};

    private string loadedTileDataFolderPath = string.Empty;

    [ObservableProperty]
    private TileDataEntry? selectedTileDataEntry;

    [ObservableProperty]
    private string tileDataSearchText = string.Empty;

    [ObservableProperty]
    private bool showLandTileData = true;

    [ObservableProperty]
    private bool showItemTileData = true;

    public ICommand RefreshTileDataCommand { get; private set; } = null!;

    private void ResetTileDataForProfileChange()
    {
        TileDataEntries.Clear();
        FilteredTileDataEntries.Clear();
        SelectedTileDataFlagOptions.Clear();
        SelectedTileDataEntry = null;
        loadedTileDataFolderPath = string.Empty;
    }

    private void InitializeTileDataCommands()
    {
        RefreshTileDataCommand = new RelayCommand(LoadTileData);
    }

    private void LoadTileData()
    {
        TileDataEntries.Clear();
        FilteredTileDataEntries.Clear();
        SelectedTileDataFlagOptions.Clear();
        SelectedTileDataEntry = null;

        string folderPath = GetCurrentFolderPath();
        loadedTileDataFolderPath = folderPath;

        string tileDataPath = Path.Combine(folderPath, "tiledata.mul");

        if (!File.Exists(tileDataPath))
        {
            StatusText = "tiledata.mul was not found.";
            return;
        }

        List<TileDataEntry> entries = tileDataMulService.Load(tileDataPath);

        foreach (TileDataEntry entry in entries)
        {
            TileDataEntries.Add(entry);
        }

        ApplyTileDataFilter();
        StatusText = "Loaded tiledata entries: " + TileDataEntries.Count;
    }

    private void ApplyTileDataFilter()
    {
        FilteredTileDataEntries.Clear();

        string search = TileDataSearchText.Trim();

        IEnumerable<TileDataEntry> query = TileDataEntries;

        if (!ShowLandTileData)
        {
            query = query.Where(x => !x.IsLand);
        }

        if (!ShowItemTileData)
        {
            query = query.Where(x => x.IsLand);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(x =>
                x.Id.ToString().Contains(search, StringComparison.OrdinalIgnoreCase) ||
                x.IdText.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                x.Name.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        foreach (TileDataEntry entry in query)
        {
            FilteredTileDataEntries.Add(entry);
        }

        if (FilteredTileDataEntries.Count > 0)
        {
            SelectedTileDataEntry ??= FilteredTileDataEntries[0];
        }
        else
        {
            SelectedTileDataEntry = null;
        }
    }

    partial void OnTileDataSearchTextChanged(string value)
    {
        ApplyTileDataFilter();
    }

    partial void OnShowLandTileDataChanged(bool value)
    {
        ApplyTileDataFilter();
    }

    partial void OnShowItemTileDataChanged(bool value)
    {
        ApplyTileDataFilter();
    }

    partial void OnSelectedTileDataEntryChanged(TileDataEntry? value)
    {
        RebuildSelectedTileDataFlags();
    }

    public void RebuildSelectedTileDataFlags()
    {
        SelectedTileDataFlagOptions.Clear();

        ulong flags = SelectedTileDataEntry?.Flags ?? 0;

        for (int i = 0; i < TileDataFlagNames.Length; i++)
        {
            ulong mask = 1UL << i;

            SelectedTileDataFlagOptions.Add(new TileDataFlagOption
            {
                BitIndex = i,
                Name = TileDataFlagNames[i],
                Description = "0x" + mask.ToString("X16"),
                IsChecked = (flags & mask) != 0
            });
        }
    }

    [RelayCommand]
    private void ToggleSelectedTileDataFlag(TileDataFlagOption? option)
    {
        if (SelectedTileDataEntry == null || option == null)
        {
            return;
        }

        if (option.IsChecked)
        {
            SelectedTileDataEntry.Flags |= option.Mask;
        }
        else
        {
            SelectedTileDataEntry.Flags &= ~option.Mask;
        }

        SelectedTileDataEntry.IsEdited = true;

        OnPropertyChanged(nameof(SelectedTileDataEntry));
    }

    [RelayCommand]
    private void SaveTileDataChanges()
    {
        List<TileDataEntry> editedEntries = TileDataEntries
            .Where(entry => entry.IsEdited)
            .ToList();

        if (editedEntries.Count == 0)
        {
            StatusText = "No TileData changes to save.";
            return;
        }

        string folderPath = GetCurrentFolderPath();
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            StatusText = "Choose a UO folder first.";
            return;
        }

        bool success = tileDataMulService.SaveTileData(folderPath, TileDataEntries.ToList(), out string message);
        StatusText = message;

        if (!success)
        {
            return;
        }

        foreach (TileDataEntry entry in editedEntries)
        {
            entry.IsEdited = false;
        }
    }
}
