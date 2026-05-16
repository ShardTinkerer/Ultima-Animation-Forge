using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UltimaAnimationForge.Models;
using UltimaAnimationForge.Services;

namespace UltimaAnimationForge.ViewModels;

public partial class MainWindowViewModel
{
    private readonly ArtDataService artDataService = new();

    public ObservableCollection<ArtEntry> ArtEntries { get; } = new();

    private DispatcherTimer? artAnimDataPlaybackTimer;
    private int artAnimDataPlaybackIndex;
    private bool isArtAnimDataPlaying;

    public string ArtAnimDataPlayButtonText => isArtAnimDataPlaying ? "Pause" : "Play";

    [ObservableProperty]
    private string selectedArtAnimDataStatusText = string.Empty;

    [ObservableProperty]
    private ArtEntry? selectedArtEntry;

    [ObservableProperty]
    private WriteableBitmap? selectedArtBitmap;

    [ObservableProperty]
    private string artSearchText = string.Empty;

    [ObservableProperty]
    private bool showLandArt = false;

    [ObservableProperty]
    private bool showStaticArt = true;

    [ObservableProperty]
    private string artStatusText = "Art not loaded.";

    [ObservableProperty]
    private bool showArtThumbnails = true;

    [ObservableProperty]
    private bool showFreeArtSlots;

    public string SelectedArtTileDataFlagNames =>
    SelectedArtTileDataEntry == null
        ? "-"
        : BuildTileDataFlagText(SelectedArtTileDataEntry.Flags);

    private static string BuildTileDataFlagText(ulong flags)
    {
        if (flags == 0)
        {
            return "None";
        }

        List<string> names = new();

        for (int i = 0; i < TileDataFlagNames.Length; i++)
        {
            ulong mask = 1UL << i;

            if ((flags & mask) != 0)
            {
                names.Add(TileDataFlagNames[i]);
            }
        }

        return names.Count == 0 ? "None" : string.Join(", ", names);
    }

    public string SelectedArtAnimationGumpText
    {
        get
        {
            if (SelectedArtTileDataEntry == null)
            {
                return "Animation/Gump: -";
            }

            int animation = SelectedArtTileDataEntry.Animation;

            if (SelectedArtTileDataEntry.IsLand || animation <= 0)
            {
                return "Animation/Gump: " + animation;
            }

            int maleGump = 50000 + animation;

            return "Animation/Gump: " +
                   animation +
                   " / Male Gump " +
                   maleGump +
                   " [0x" + maleGump.ToString("X") + "]";
        }
    }

    public int SelectedArtMaleEquipmentGumpId =>
    SelectedArtTileDataEntry != null && !SelectedArtTileDataEntry.IsLand && SelectedArtTileDataEntry.Animation > 0
        ? 50000 + SelectedArtTileDataEntry.Animation
        : -1;

    public string SelectedArtMaleEquipmentGumpText =>
        SelectedArtMaleEquipmentGumpId >= 0
            ? "Male equipment gump: " + SelectedArtMaleEquipmentGumpId + " [0x" + SelectedArtMaleEquipmentGumpId.ToString("X") + "]"
            : "Male equipment gump: -";

    public bool ShowSelectedArtEquipmentGump =>
        SelectedArtMaleEquipmentGumpBitmap != null;

    [ObservableProperty]
    private WriteableBitmap? selectedArtMaleEquipmentGumpBitmap;

    public AnimDataEntry? SelectedArtAnimDataEntry
    {
        get
        {
            if (SelectedArtEntry == null)
            {
                return null;
            }

            if (!string.Equals(SelectedArtEntry.Type, "Static", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return animDataMulService.AllLoadedEntries.FirstOrDefault(entry => entry.Id == SelectedArtEntry.ArtId);
        }
    }

    public bool ShowSelectedArtAnimDataPreview =>
        SelectedArtAnimDataEntry != null &&
        SelectedArtTileDataEntry != null &&
        (SelectedArtTileDataEntry.Flags & 0x01000000UL) != 0;

    public ObservableCollection<AnimDataFrameEntry> SelectedArtAnimDataFrames { get; } = new();

    [ObservableProperty]
    private AnimDataFrameEntry? selectedArtAnimDataFrame;

    [ObservableProperty]
    private WriteableBitmap? selectedArtAnimDataFrameBitmap;

    public TileDataEntry? SelectedArtTileDataEntry
    {
        get
        {
            if (SelectedArtEntry == null)
            {
                return null;
            }

            return TileDataEntries.FirstOrDefault(entry =>
                entry.IsLand == string.Equals(SelectedArtEntry.Type, "Land", StringComparison.OrdinalIgnoreCase) &&
                entry.Id == SelectedArtEntry.ArtId);
        }
    }

    [RelayCommand]
    private void LoadArtTab()
    {
        string folderPath = GetCurrentFolderPath();

        if (string.IsNullOrWhiteSpace(folderPath))
        {
            ArtStatusText = "Choose a UO folder first.";
            return;
        }

        if (TileDataEntries.Count == 0)
        {
            LoadTileData();
        }

        if (!animDataMulService.IsLoaded)
        {
            animDataMulService.Initialize(folderPath);
        }

        if (!artDataService.Initialize(folderPath))
        {
            ArtEntries.Clear();
            SelectedArtBitmap = null;
            ArtStatusText = "Could not find artLegacyMUL.uop or art.mul/artidx.mul.";
            return;
        }

        RebuildArtEntries();
    }

    [RelayCommand]
    private void RebuildArtEntries()
    {
        ArtEntries.Clear();

        foreach (ArtEntry entry in artDataService.BuildEntries(
             ShowLandArt,
             ShowStaticArt,
             ShowFreeArtSlots,
             ArtSearchText))
        {
            if (showArtThumbnails)
            {
                entry.Thumbnail = artDataService.LoadThumbnail(entry);
            }

            ArtEntries.Add(entry);
        }

        if (ArtEntries.Count > 0)
        {
            SelectedArtEntry = ArtEntries[0];
        }
        else
        {
            SelectedArtEntry = null;
            SelectedArtBitmap = null;
        }

        ArtStatusText = "Loaded " + ArtEntries.Count + " art entries.";
    }

    partial void OnSelectedArtEntryChanged(ArtEntry? value)
    {
        SelectedArtBitmap = artDataService.LoadBitmap(value);

        if (value != null)
        {
            ArtStatusText = value.DisplayText;
        }

        RebuildSelectedArtAnimDataFrames();
        LoadSelectedArtEquipmentGump();

        OnPropertyChanged(nameof(SelectedArtTileDataEntry));
        OnPropertyChanged(nameof(SelectedArtAnimDataEntry));
        OnPropertyChanged(nameof(ShowSelectedArtAnimDataPreview));
        OnPropertyChanged(nameof(SelectedArtMaleEquipmentGumpId));
        OnPropertyChanged(nameof(SelectedArtMaleEquipmentGumpText));
        OnPropertyChanged(nameof(ShowSelectedArtEquipmentGump));
        OnPropertyChanged(nameof(SelectedArtTileDataFlagNames));
        OnPropertyChanged(nameof(SelectedArtAnimationGumpText));
    }

    partial void OnArtSearchTextChanged(string value)
    {
        RebuildArtEntries();
    }

    partial void OnShowLandArtChanged(bool value)
    {
        RebuildArtEntries();
    }

    partial void OnShowStaticArtChanged(bool value)
    {
        RebuildArtEntries();
    }

    [RelayCommand]
    private async Task ExportSelectedArtAsync()
    {
        if (SelectedArtEntry == null || SelectedArtBitmap == null)
        {
            ArtStatusText = "No art entry selected.";
            return;
        }

        Window? mainWindow = GetMainWindow();
        if (mainWindow == null)
        {
            ArtStatusText = "Could not locate main window.";
            return;
        }

        IStorageFile? file = await mainWindow.StorageProvider.SaveFilePickerAsync(
            new FilePickerSaveOptions
            {
                Title = "Export Selected Art",
                SuggestedFileName = SelectedArtEntry.Type.ToLowerInvariant() + "_0x" + SelectedArtEntry.ArtId.ToString("X4") + ".png",
                FileTypeChoices = new[]
                {
                new FilePickerFileType("PNG Image") { Patterns = new[] { "*.png" } },
                new FilePickerFileType("BMP Image") { Patterns = new[] { "*.bmp" } }
                }
            });

        if (file == null)
        {
            ArtStatusText = "Export cancelled.";
            return;
        }

        string? path = file.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            ArtStatusText = "Selected export path is invalid.";
            return;
        }

        artDataService.ExportBitmap(SelectedArtBitmap, path);
        ArtStatusText = "Exported " + SelectedArtEntry.DisplayText + ".";
    }

    [RelayCommand]
    private async Task MassExportArtAsync()
    {
        if (ArtEntries.Count == 0)
        {
            ArtStatusText = "No art entries loaded.";
            return;
        }

        Window? mainWindow = GetMainWindow();
        if (mainWindow == null)
        {
            ArtStatusText = "Could not locate main window.";
            return;
        }

        IStorageFolder? folder = await mainWindow.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title = "Choose Art Export Folder",
                AllowMultiple = false
            }).ContinueWith(task => task.Result.Count > 0 ? task.Result[0] : null);

        if (folder == null)
        {
            ArtStatusText = "Mass export cancelled.";
            return;
        }

        string? folderPath = folder.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            ArtStatusText = "Selected export folder is invalid.";
            return;
        }

        int exported = 0;

        List<ArtEntry> entriesToExport = ArtEntries
    .Where(entry => entry.IsChecked)
    .ToList();

        if (entriesToExport.Count == 0)
        {
            ArtStatusText = "No checked art entries to export.";
            return;
        }

        foreach (ArtEntry entry in entriesToExport)
        {
            WriteableBitmap? bitmap = artDataService.LoadBitmap(entry);
            if (bitmap == null)
            {
                continue;
            }

            string fileName = entry.Type.ToLowerInvariant() + "_0x" + entry.ArtId.ToString("X4") + ".png";
            string path = Path.Combine(folderPath, fileName);

            artDataService.ExportBitmap(bitmap, path);
            exported++;
        }

        ArtStatusText = "Mass exported " + exported + " checked art entries.";
    }

    partial void OnShowArtThumbnailsChanged(bool value)
    {
        RebuildArtEntries();
    }

    [RelayCommand]
    private void CheckAllArt()
    {
        foreach (ArtEntry entry in ArtEntries)
        {
            entry.IsChecked = true;
        }

        ArtStatusText = "Checked " + ArtEntries.Count + " art entries.";
    }

    [RelayCommand]
    private void UncheckAllArt()
    {
        foreach (ArtEntry entry in ArtEntries)
        {
            entry.IsChecked = false;
        }

        ArtStatusText = "Unchecked all art entries.";
    }

    partial void OnShowFreeArtSlotsChanged(bool value)
    {
        RebuildArtEntries();
    }

    private void RebuildSelectedArtAnimDataFrames()
    {
        SelectedArtAnimDataFrames.Clear();
        SelectedArtAnimDataFrame = null;
        SelectedArtAnimDataFrameBitmap = null;
        artAnimDataPlaybackTimer?.Stop();
        isArtAnimDataPlaying = false;
        artAnimDataPlaybackIndex = 0;
        OnPropertyChanged(nameof(ArtAnimDataPlayButtonText));

        AnimDataEntry? animEntry = SelectedArtAnimDataEntry;
        if (animEntry == null)
        {
            return;
        }

        animEntry.RebuildFrames(
    GetStaticTileName,
    StaticArtExists,
    graphicId => LoadStaticArtBitmap(graphicId));

        bool hasAnyFrameArt = animEntry.Frames.Any(frame => frame.Bitmap != null);
        bool hasSelectedArt = SelectedArtBitmap != null;

        selectedArtAnimDataStatusText =
            "Frames: " + animEntry.FrameCount +
            " | Interval: " + animEntry.FrameInterval +
            " | Start: " + animEntry.FrameStart +
            ((hasSelectedArt || hasAnyFrameArt) ? string.Empty : " | Missing art");

        foreach (AnimDataFrameEntry frame in animEntry.Frames)
        {
            SelectedArtAnimDataFrames.Add(frame);
        }

        if (SelectedArtAnimDataFrames.Count > 0)
        {
            SelectedArtAnimDataFrame = SelectedArtAnimDataFrames[0];
        }
    }

    partial void OnSelectedArtAnimDataFrameChanged(AnimDataFrameEntry? value)
    {
        SelectedArtAnimDataFrameBitmap = value?.Bitmap;

        if (!isArtAnimDataPlaying && value?.Bitmap != null)
        {
            SelectedArtBitmap = value.Bitmap;
        }
    }

    [RelayCommand]
    private void ToggleArtAnimDataPlayback()
    {
        if (SelectedArtAnimDataFrames.Count == 0)
        {
            return;
        }

        if (artAnimDataPlaybackTimer == null)
        {
            artAnimDataPlaybackTimer = new DispatcherTimer();
            artAnimDataPlaybackTimer.Tick += ArtAnimDataPlaybackTimer_Tick;
        }

        if (isArtAnimDataPlaying)
        {
            artAnimDataPlaybackTimer.Stop();
            isArtAnimDataPlaying = false;
            OnPropertyChanged(nameof(ArtAnimDataPlayButtonText));
            return;
        }

        isArtAnimDataPlaying = true;
        OnPropertyChanged(nameof(ArtAnimDataPlayButtonText));

        int interval = 100;
        if (SelectedArtAnimDataEntry != null && SelectedArtAnimDataEntry.FrameInterval > 0)
        {
            interval = Math.Max(50, SelectedArtAnimDataEntry.FrameInterval * 100);
        }

        artAnimDataPlaybackTimer.Interval = TimeSpan.FromMilliseconds(interval);
        artAnimDataPlaybackTimer.Start();
    }

    private void ArtAnimDataPlaybackTimer_Tick(object? sender, EventArgs e)
    {
        if (SelectedArtAnimDataFrames.Count == 0)
        {
            artAnimDataPlaybackTimer?.Stop();
            isArtAnimDataPlaying = false;
            OnPropertyChanged(nameof(ArtAnimDataPlayButtonText));
            return;
        }

        artAnimDataPlaybackIndex++;

        if (artAnimDataPlaybackIndex >= SelectedArtAnimDataFrames.Count)
        {
            artAnimDataPlaybackIndex = 0;
        }

        SelectedArtAnimDataFrame = SelectedArtAnimDataFrames[artAnimDataPlaybackIndex];

        if (SelectedArtAnimDataFrame?.Bitmap != null)
        {
            SelectedArtBitmap = SelectedArtAnimDataFrame.Bitmap;
        }
    }

    private void LoadSelectedArtEquipmentGump()
    {
        SelectedArtMaleEquipmentGumpBitmap = null;

        int gumpId = SelectedArtMaleEquipmentGumpId;
        if (gumpId < 0)
        {
            return;
        }

        string folderPath = GetCurrentFolderPath();
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return;
        }

        if (gumpDataService.Entries.Count == 0)
        {
            gumpDataService.Initialize(folderPath);
        }

        GumpEntry? gumpEntry = gumpDataService.Entries
            .FirstOrDefault(entry => entry.GumpId == gumpId && entry.IsValid);

        if (gumpEntry == null)
        {
            return;
        }

        GumpLoadResult result = gumpDataService.LoadGump(gumpEntry);
        if (!result.Success || result.Bitmap == null)
        {
            return;
        }

        SelectedArtMaleEquipmentGumpBitmap = result.Bitmap;
    }

    [RelayCommand]
    private async Task ImportSelectedArtAsync()
    {
        if (SelectedArtEntry == null)
        {
            ArtStatusText = "No art entry selected.";
            return;
        }

        Window? mainWindow = GetMainWindow();
        if (mainWindow == null)
        {
            ArtStatusText = "Could not locate main window.";
            return;
        }

        IReadOnlyList<IStorageFile> files = await mainWindow.StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = "Import Art Image",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                new FilePickerFileType("Image files")
                {
                    Patterns = new[] { "*.png", "*.bmp" }
                }
                }
            });

        if (files.Count == 0)
        {
            ArtStatusText = "Import cancelled.";
            return;
        }

        string? path = files[0].TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            ArtStatusText = "Selected image path is invalid.";
            return;
        }

        bool success = artDataService.ImportBitmapToArt(SelectedArtEntry, path, out string message);
        ArtStatusText = message;

        if (!success)
        {
            return;
        }

        SelectedArtBitmap = artDataService.LoadBitmap(SelectedArtEntry);
        SelectedArtEntry.Thumbnail = artDataService.LoadThumbnail(SelectedArtEntry);

        RebuildArtEntries();
    }

    [RelayCommand]
    private async Task MassImportArtAsync()
    {
        Window? mainWindow = GetMainWindow();
        if (mainWindow == null)
        {
            ArtStatusText = "Could not locate main window.";
            return;
        }

        IReadOnlyList<IStorageFolder> folders = await mainWindow.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title = "Choose Art Import Folder",
                AllowMultiple = false
            });

        if (folders.Count == 0)
        {
            ArtStatusText = "Mass import cancelled.";
            return;
        }

        string? folderPath = folders[0].TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
        {
            ArtStatusText = "Selected import folder is invalid.";
            return;
        }

        Dictionary<int, ArtEntry> entriesByStaticId = ArtEntries
            .Where(entry => string.Equals(entry.Type, "Static", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(entry => entry.ArtId, entry => entry);

        Dictionary<int, ArtEntry> entriesByLandId = ArtEntries
            .Where(entry => string.Equals(entry.Type, "Land", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(entry => entry.ArtId, entry => entry);

        string[] imagePaths = Directory.GetFiles(folderPath, "*.*", SearchOption.TopDirectoryOnly)
            .Where(path =>
                string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Path.GetExtension(path), ".bmp", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        int imported = 0;
        int skipped = 0;
        int failed = 0;
        string lastError = string.Empty;

        foreach (string imagePath in imagePaths)
        {
            if (!TryParseArtImportFileName(Path.GetFileNameWithoutExtension(imagePath), out bool isLand, out int artId))
            {
                skipped++;
                continue;
            }

            Dictionary<int, ArtEntry> lookup = isLand ? entriesByLandId : entriesByStaticId;

            if (!lookup.TryGetValue(artId, out ArtEntry? entry))
            {
                skipped++;
                continue;
            }

            bool success = artDataService.ImportBitmapToArt(entry, imagePath, out string message);
            if (success)
            {
                imported++;
            }
            else
            {
                failed++;
                lastError = message;
            }
        }

        RebuildArtEntries();

        ArtStatusText =
            "Mass import complete. Imported " + imported +
            ", skipped " + skipped +
            ", failed " + failed +
            (string.IsNullOrWhiteSpace(lastError) ? "." : ". Last error: " + lastError);
    }

    private static bool TryParseArtImportFileName(string fileName, out bool isLand, out int artId)
    {
        isLand = false;
        artId = -1;

        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        string text = fileName.Trim();

        if (text.StartsWith("Land_", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("land-", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("land ", StringComparison.OrdinalIgnoreCase))
        {
            isLand = true;
            text = text.Substring(5).TrimStart('_', '-', ' ');
        }
        else if (text.StartsWith("Static_", StringComparison.OrdinalIgnoreCase) ||
                 text.StartsWith("static-", StringComparison.OrdinalIgnoreCase) ||
                 text.StartsWith("static ", StringComparison.OrdinalIgnoreCase) ||
                 text.StartsWith("Item_", StringComparison.OrdinalIgnoreCase) ||
                 text.StartsWith("item-", StringComparison.OrdinalIgnoreCase) ||
                 text.StartsWith("item ", StringComparison.OrdinalIgnoreCase))
        {
            isLand = false;
            text = text.Substring(text.IndexOfAny(new[] { '_', '-', ' ' }) + 1).Trim();
        }
        else
        {
            isLand = false;
        }

        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return int.TryParse(
                text.Substring(2),
                System.Globalization.NumberStyles.HexNumber,
                null,
                out artId);
        }

        return int.TryParse(text, out artId);
    }

    [RelayCommand]
    private void SaveArtChanges()
    {
        bool success = artDataService.SavePendingArtChanges(out string message);
        ArtStatusText = message;

        if (success)
        {
            RebuildArtEntries();
        }
    }
}
