using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using UltimaAnimationForge.Models;

namespace UltimaAnimationForge.Services;

public sealed class ArtDataService
{
    private readonly Dictionary<ulong, UopFileData> pendingUopArtEdits = new();

    public bool HasPendingArtChanges => pendingUopArtEdits.Count > 0;
    public int PendingArtChangeCount => pendingUopArtEdits.Count;

    private const int LandCount = 0x4000;
    private const int StaticOffset = 0x4000;
    private const int MaxArtEntries = 0x14000;

    private string folderPath = string.Empty;
    private string artMulPath = string.Empty;
    private string artIdxPath = string.Empty;

    private string artLegacyUopPath = string.Empty;
    private UopFileReader? artUopReader;
    private bool useUop;

    public bool Initialize(string uoFolderPath)
    {
        string newFolderPath = uoFolderPath ?? string.Empty;

        if (!string.Equals(folderPath, newFolderPath, StringComparison.OrdinalIgnoreCase))
        {
            pendingUopArtEdits.Clear();
        }

        folderPath = newFolderPath;

        artLegacyUopPath = Path.Combine(folderPath, "artLegacyMUL.uop");
        artMulPath = Path.Combine(folderPath, "art.mul");
        artIdxPath = Path.Combine(folderPath, "artidx.mul");

        useUop = false;
        artUopReader = null;

        if (File.Exists(artLegacyUopPath))
        {
            artUopReader = new UopFileReader(artLegacyUopPath);
            useUop = artUopReader.Load();
            return useUop;
        }

        return File.Exists(artMulPath) && File.Exists(artIdxPath);
    }

    private static byte[] StripUopArtHeader(byte[] data)
    {
        if (data == null || data.Length <= 4)
        {
            return Array.Empty<byte>();
        }

        byte[] output = new byte[data.Length - 4];
        Buffer.BlockCopy(data, 4, output, 0, output.Length);
        return output;
    }

    public List<ArtEntry> BuildEntries(bool includeLand, bool includeStatics, bool includeFreeSlots, string searchText)
    {
        if (useUop)
        {
            return BuildUopEntries(includeLand, includeStatics, includeFreeSlots, searchText);
        }

        List<ArtEntry> entries = new();

        if (!File.Exists(artIdxPath))
        {
            return entries;
        }

        int idxCount = (int)(new FileInfo(artIdxPath).Length / 12);
        int maxCount = Math.Min(idxCount, MaxArtEntries);

        for (int fileIndex = 0; fileIndex < maxCount; fileIndex++)
        {
            bool isLand = fileIndex < StaticOffset;
            if (isLand && !includeLand)
            {
                continue;
            }

            if (!isLand && !includeStatics)
            {
                continue;
            }

            if (!TryReadIndex(fileIndex, out int lookup, out int length, out int extra))
            {
                continue;
            }

            if (lookup < 0 || length <= 0)
            {
                continue;
            }

            int artId = isLand ? fileIndex : fileIndex - StaticOffset;

            if (!string.IsNullOrWhiteSpace(searchText))
            {
                string search = searchText.Trim();

                bool match =
                    artId.ToString().Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    ("0x" + artId.ToString("X4")).Contains(search, StringComparison.OrdinalIgnoreCase);

                if (!match)
                {
                    continue;
                }
            }

            entries.Add(new ArtEntry
            {
                ArtId = artId,
                FileIndex = fileIndex,
                Type = isLand ? "Land" : "Static",
                SecondaryText = "Offset " + lookup + " | Length " + length + " | Extra " + extra
            });
        }

        return entries;
    }

    public WriteableBitmap? LoadBitmap(ArtEntry? entry)
    {
        if (entry == null)
        {
            return null;
        }

        if (useUop)
        {
            return LoadUopBitmap(entry);
        }

        if (!TryReadIndex(entry.FileIndex, out int lookup, out int length, out _))
        {
            return null;
        }

        if (lookup < 0 || length <= 0 || !File.Exists(artMulPath))
        {
            return null;
        }

        byte[] data;

        using (FileStream stream = File.OpenRead(artMulPath))
        {
            if (lookup >= stream.Length)
            {
                return null;
            }

            stream.Seek(lookup, SeekOrigin.Begin);
            int safeLength = (int)Math.Min(length, stream.Length - lookup);
            data = new byte[safeLength];
            stream.ReadExactly(data, 0, safeLength);
        }

        return entry.FileIndex < StaticOffset
            ? DecodeLand(data)
            : DecodeStatic(data);
    }

    private bool TryReadIndex(int fileIndex, out int lookup, out int length, out int extra)
    {
        lookup = -1;
        length = 0;
        extra = -1;

        if (!File.Exists(artIdxPath))
        {
            return false;
        }

        using FileStream stream = File.OpenRead(artIdxPath);
        long offset = fileIndex * 12L;

        if (offset + 12 > stream.Length)
        {
            return false;
        }

        stream.Seek(offset, SeekOrigin.Begin);

        using BinaryReader reader = new(stream);
        lookup = reader.ReadInt32();
        length = reader.ReadInt32();
        extra = reader.ReadInt32();

        return true;
    }

    private static WriteableBitmap DecodeLand(byte[] data)
    {
        const int width = 44;
        const int height = 44;

        byte[] pixels = new byte[width * height * 4];

        int source = 0;
        int xOffset = 21;
        int xRun = 2;

        for (int y = 0; y < 22; y++, xOffset--, xRun += 2)
        {
            for (int x = 0; x < xRun; x++)
            {
                if (source + 1 >= data.Length)
                {
                    break;
                }

                ushort color = BitConverter.ToUInt16(data, source);
                source += 2;

                Write1555(pixels, width, xOffset + x, y, (ushort)(color | 0x8000));
            }
        }

        xOffset = 0;
        xRun = 44;

        for (int y = 22; y < 44; y++, xOffset++, xRun -= 2)
        {
            for (int x = 0; x < xRun; x++)
            {
                if (source + 1 >= data.Length)
                {
                    break;
                }

                ushort color = BitConverter.ToUInt16(data, source);
                source += 2;

                Write1555(pixels, width, xOffset + x, y, (ushort)(color | 0x8000));
            }
        }

        return CreateBitmap(width, height, pixels);
    }

    private static WriteableBitmap? DecodeStatic(byte[] data)
    {
        if (data.Length < 8)
        {
            return null;
        }

        int count = 2;
        ushort[] words = BytesToUShorts(data);

        int width = words[count++];
        int height = words[count++];

        if (width <= 0 || height <= 0 || width > 4096 || height > 4096)
        {
            return null;
        }

        int[] lookups = new int[height];
        int start = height + 4;

        for (int i = 0; i < height; i++)
        {
            if (count >= words.Length)
            {
                return null;
            }

            lookups[i] = start + words[count++];
        }

        byte[] pixels = new byte[width * height * 4];

        for (int y = 0; y < height; y++)
        {
            count = lookups[y];

            int x = 0;

            while (count + 1 < words.Length)
            {
                int xOffset = words[count++];
                int xRun = words[count++];

                if (xOffset + xRun == 0)
                {
                    break;
                }

                x += xOffset;

                for (int i = 0; i < xRun; i++)
                {
                    if (count >= words.Length)
                    {
                        break;
                    }

                    ushort color = (ushort)(words[count++] ^ 0x8000);
                    Write1555(pixels, width, x + i, y, color);
                }

                x += xRun;
            }
        }

        return CreateBitmap(width, height, pixels);
    }

    private static ushort[] BytesToUShorts(byte[] data)
    {
        ushort[] result = new ushort[data.Length / 2];

        for (int i = 0; i < result.Length; i++)
        {
            result[i] = BitConverter.ToUInt16(data, i * 2);
        }

        return result;
    }

    private static void Write1555(byte[] pixels, int width, int x, int y, ushort color)
    {
        if (x < 0 || y < 0)
        {
            return;
        }

        int offset = ((y * width) + x) * 4;
        if (offset < 0 || offset + 3 >= pixels.Length)
        {
            return;
        }

        if ((color & 0x8000) == 0)
        {
            pixels[offset + 3] = 0;
            return;
        }

        byte r = (byte)(((color >> 10) & 0x1F) * 255 / 31);
        byte g = (byte)(((color >> 5) & 0x1F) * 255 / 31);
        byte b = (byte)((color & 0x1F) * 255 / 31);

        pixels[offset + 0] = b;
        pixels[offset + 1] = g;
        pixels[offset + 2] = r;
        pixels[offset + 3] = 255;
    }

    private static WriteableBitmap CreateBitmap(int width, int height, byte[] pixels)
    {
        WriteableBitmap bitmap = new(
            new PixelSize(width, height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);

        using ILockedFramebuffer framebuffer = bitmap.Lock();
        Marshal.Copy(pixels, 0, framebuffer.Address, pixels.Length);

        return bitmap;
    }

    private List<ArtEntry> BuildUopEntries(bool includeLand, bool includeStatics, bool includeFreeSlots, string searchText)
    {

        List<ArtEntry> entries = new();

        if (artUopReader == null || !artUopReader.IsLoaded)
        {
            return entries;
        }

        for (int fileIndex = 0; fileIndex < MaxArtEntries; fileIndex++)
        {
            bool isLand = fileIndex < StaticOffset;

            if (isLand && !includeLand)
            {
                continue;
            }

            if (!isLand && !includeStatics)
            {
                continue;
            }

            string virtualPath = GetArtUopVirtualPath(fileIndex);
            ulong hash = UopFileReader.CreateHash(virtualPath);

            UopDataHeader? header = artUopReader.GetEntryByHash(hash);
            bool isFreeSlot = !header.HasValue;

            if (isFreeSlot && !includeFreeSlots)
            {
                continue;
            }

            int artId = isLand ? fileIndex : fileIndex - StaticOffset;

            if (!string.IsNullOrWhiteSpace(searchText))
            {
                string search = searchText.Trim();

                bool match =
                    artId.ToString().Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    ("0x" + artId.ToString("X4")).Contains(search, StringComparison.OrdinalIgnoreCase);

                if (!match)
                {
                    continue;
                }
            }

            entries.Add(new ArtEntry
            {
                ArtId = artId,
                FileIndex = fileIndex,
                Type = isLand ? "Land" : "Static",
                IsFreeSlot = isFreeSlot,
                SecondaryText = isFreeSlot
                    ? "Free slot"
                    : "artLegacyMUL.uop | Offset " + header.Value.Offset +
                      " | Size " + header.Value.DecompressedSize
            });
        }

        return entries;
    }

    private WriteableBitmap? LoadUopBitmap(ArtEntry? entry)
    {
        if (entry == null || artUopReader == null || !artUopReader.IsLoaded)
        {
            return null;
        }

        if (entry.IsFreeSlot)
        {
            return null;
        }

        string virtualPath = GetArtUopVirtualPath(entry.FileIndex);
        ulong hash = UopFileReader.CreateHash(virtualPath);

        if (pendingUopArtEdits.TryGetValue(hash, out UopFileData? pendingEdit))
        {
            byte[] pendingData = pendingEdit.Data;

            if (entry.FileIndex < StaticOffset)
            {
                pendingData = StripUopArtHeader(pendingData);
                return DecodeLand(pendingData);
            }

            return DecodeStatic(pendingData);
        }

        UopDataHeader? header = artUopReader.GetEntryByHash(hash);
        if (!header.HasValue)
        {
            return null;
        }

        byte[]? data = artUopReader.ReadData(header.Value);
        if (data == null || data.Length == 0)
        {
            return null;
        }

        if (entry.FileIndex < StaticOffset)
        {
            data = StripUopArtHeader(data);
            return DecodeLand(data);
        }

        return DecodeStatic(data);
    }
    private static string GetArtUopVirtualPath(int fileIndex)
    {
        return "build/artlegacymul/" + fileIndex.ToString("D8") + ".tga";
    }

    public WriteableBitmap? LoadThumbnail(ArtEntry? entry)
    {
        WriteableBitmap? bitmap = LoadBitmap(entry);
        if (bitmap == null)
        {
            return null;
        }

        return bitmap;
    }

    public void ExportBitmap(WriteableBitmap bitmap, string path)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();

        if (extension == ".bmp")
        {
            SaveBitmapAsBmp(bitmap, path);
            return;
        }

        bitmap.Save(path);
    }

    private static void SaveBitmapAsBmp(WriteableBitmap bitmap, string path)
    {
        using ILockedFramebuffer framebuffer = bitmap.Lock();

        int width = framebuffer.Size.Width;
        int height = framebuffer.Size.Height;
        int srcStride = framebuffer.RowBytes;
        int dstStride = width * 4;

        byte[] src = new byte[srcStride * height];
        Marshal.Copy(framebuffer.Address, src, 0, src.Length);

        int pixelDataSize = dstStride * height;
        int fileSize = 54 + pixelDataSize;

        using FileStream stream = File.Create(path);
        using BinaryWriter writer = new(stream);

        writer.Write((byte)'B');
        writer.Write((byte)'M');
        writer.Write(fileSize);
        writer.Write(0);
        writer.Write(54);

        writer.Write(40);
        writer.Write(width);
        writer.Write(height);
        writer.Write((short)1);
        writer.Write((short)32);
        writer.Write(0);
        writer.Write(pixelDataSize);
        writer.Write(2835);
        writer.Write(2835);
        writer.Write(0);
        writer.Write(0);

        for (int y = height - 1; y >= 0; y--)
        {
            writer.Write(src, y * srcStride, dstStride);
        }
    }

    public ArtEntry? GetStaticEntryById(int artId)
    {
        if (artId < 0)
        {
            return null;
        }

        int fileIndex = artId + 0x4000;

        if (useUop)
        {
            if (artUopReader == null || !artUopReader.IsLoaded)
            {
                return null;
            }

            string virtualPath = GetArtUopVirtualPath(fileIndex);
            ulong hash = UopFileReader.CreateHash(virtualPath);
            UopDataHeader? header = artUopReader.GetEntryByHash(hash);

            if (!header.HasValue)
            {
                return null;
            }

            return new ArtEntry
            {
                ArtId = artId,
                FileIndex = fileIndex,
                Type = "Static",
                IsFreeSlot = false,
                SecondaryText = "artLegacyMUL.uop | Offset " + header.Value.Offset +
                                " | Size " + header.Value.DecompressedSize
            };
        }

        if (!TryReadIndex(fileIndex, out int lookup, out int length, out int extra))
        {
            return null;
        }

        if (lookup < 0 || length <= 0)
        {
            return null;
        }

        return new ArtEntry
        {
            ArtId = artId,
            FileIndex = fileIndex,
            Type = "Static",
            IsFreeSlot = false,
            SecondaryText = "Offset " + lookup + " | Length " + length + " | Extra " + extra
        };
    }

    public bool ImportBitmapToArt(ArtEntry entry, string imagePath, out string message)
    {
        message = string.Empty;

        if (entry == null)
        {
            message = "No art entry selected.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
        {
            message = "Import image was not found.";
            return false;
        }

        if (!useUop)
        {
            message = "Art import currently supports artLegacyMUL.uop only.";
            return false;
        }

        if (artUopReader == null || !artUopReader.IsLoaded || string.IsNullOrWhiteSpace(artLegacyUopPath))
        {
            message = "artLegacyMUL.uop is not loaded.";
            return false;
        }

        try
        {
            WriteableBitmap bitmap = LoadBitmapFromFile(imagePath);

            byte[] encodedData = entry.FileIndex < StaticOffset
                ? EncodeLand(bitmap)
                : EncodeStatic(bitmap);

            string virtualPath = GetArtUopVirtualPath(entry.FileIndex);
            ulong targetHash = UopFileReader.CreateHash(virtualPath);

            pendingUopArtEdits[targetHash] = new UopFileData
            {
                Hash = targetHash,
                Data = encodedData,
                DecompressedSize = (uint)encodedData.Length,
                IsCompressed = true,
                IsEmpty = false
            };

            message = "Queued art " + entry.DisplayText + ". Pending changes: " + pendingUopArtEdits.Count + ".";
            return true;
        }
        catch (Exception exception)
        {
            message = "Import failed: " + exception.Message;
            return false;
        }
    }

    private static WriteableBitmap LoadBitmapFromFile(string path)
    {
        using FileStream stream = File.OpenRead(path);
        Bitmap source = new Bitmap(stream);

        WriteableBitmap bitmap = new(
            source.PixelSize,
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);

        using ILockedFramebuffer framebuffer = bitmap.Lock();

        source.CopyPixels(
            new PixelRect(0, 0, source.PixelSize.Width, source.PixelSize.Height),
            framebuffer.Address,
            framebuffer.RowBytes * framebuffer.Size.Height,
            framebuffer.RowBytes);

        return bitmap;
    }

    private sealed class ArtFramePixels
    {
        public int Width { get; }
        public int Height { get; }
        public byte[] Pixels { get; }

        public ArtFramePixels(int width, int height, byte[] pixels)
        {
            Width = width;
            Height = height;
            Pixels = pixels;
        }
    }

    private static ArtFramePixels ReadPixels(WriteableBitmap bitmap)
    {
        using ILockedFramebuffer framebuffer = bitmap.Lock();

        int width = framebuffer.Size.Width;
        int height = framebuffer.Size.Height;
        int srcRowBytes = framebuffer.RowBytes;
        int dstRowBytes = width * 4;

        byte[] src = new byte[srcRowBytes * height];
        Marshal.Copy(framebuffer.Address, src, 0, src.Length);

        byte[] packed = new byte[dstRowBytes * height];

        for (int y = 0; y < height; y++)
        {
            Buffer.BlockCopy(src, y * srcRowBytes, packed, y * dstRowBytes, dstRowBytes);
        }

        return new ArtFramePixels(width, height, packed);
    }

    private static ushort ConvertPixelTo1555(ArtFramePixels pixels, int x, int y)
    {
        int offset = ((y * pixels.Width) + x) * 4;

        byte blue = pixels.Pixels[offset + 0];
        byte green = pixels.Pixels[offset + 1];
        byte red = pixels.Pixels[offset + 2];
        byte alpha = pixels.Pixels[offset + 3];

        if (alpha < 16)
        {
            return 0;
        }

        return (ushort)(
            0x8000 |
            ((red >> 3) << 10) |
            ((green >> 3) << 5) |
            (blue >> 3));
    }

    private static bool IsTransparent(ArtFramePixels pixels, int x, int y)
    {
        int offset = ((y * pixels.Width) + x) * 4;
        return pixels.Pixels[offset + 3] < 16;
    }

    private static byte[] EncodeStatic(WriteableBitmap bitmap)
    {
        ArtFramePixels pixels = ReadPixels(bitmap);

        using MemoryStream output = new();
        using BinaryWriter writer = new(output);

        writer.Write(0);
        writer.Write((ushort)pixels.Width);
        writer.Write((ushort)pixels.Height);

        long lookupTablePosition = output.Position;

        for (int y = 0; y < pixels.Height; y++)
        {
            writer.Write((ushort)0);
        }

        List<ushort> lookupWords = new();

        for (int y = 0; y < pixels.Height; y++)
        {
            int rowStartWord = checked((int)(output.Position / 2));
            lookupWords.Add((ushort)(rowStartWord - (pixels.Height + 4)));

            int x = 0;

            while (x < pixels.Width)
            {
                int transparentRun = 0;

                while (x < pixels.Width && IsTransparent(pixels, x, y))
                {
                    transparentRun++;
                    x++;
                }

                if (x >= pixels.Width)
                {
                    break;
                }

                int runStart = x;
                List<ushort> colors = new();

                while (x < pixels.Width && !IsTransparent(pixels, x, y))
                {
                    colors.Add((ushort)(ConvertPixelTo1555(pixels, x, y) ^ 0x8000));
                    x++;
                }

                writer.Write((ushort)transparentRun);
                writer.Write((ushort)colors.Count);

                foreach (ushort color in colors)
                {
                    writer.Write(color);
                }
            }

            writer.Write((ushort)0);
            writer.Write((ushort)0);
        }

        long endPosition = output.Position;

        output.Seek(lookupTablePosition, SeekOrigin.Begin);
        foreach (ushort lookup in lookupWords)
        {
            writer.Write(lookup);
        }

        output.Seek(endPosition, SeekOrigin.Begin);
        return output.ToArray();
    }

    private static byte[] EncodeLand(WriteableBitmap bitmap)
    {
        ArtFramePixels pixels = ReadPixels(bitmap);

        using MemoryStream output = new();
        using BinaryWriter writer = new(output);

        writer.Write(0);

        int xOffset = 21;
        int xRun = 2;

        for (int y = 0; y < 22; y++, xOffset--, xRun += 2)
        {
            for (int x = 0; x < xRun; x++)
            {
                int sourceX = xOffset + x;
                ushort color = 0;

                if (sourceX >= 0 && sourceX < pixels.Width && y < pixels.Height)
                {
                    color = ConvertPixelTo1555(pixels, sourceX, y);
                }

                writer.Write((ushort)(color & 0x7FFF));
            }
        }

        xOffset = 0;
        xRun = 44;

        for (int y = 22; y < 44; y++, xOffset++, xRun -= 2)
        {
            for (int x = 0; x < xRun; x++)
            {
                int sourceX = xOffset + x;
                ushort color = 0;

                if (sourceX >= 0 && sourceX < pixels.Width && y < pixels.Height)
                {
                    color = ConvertPixelTo1555(pixels, sourceX, y);
                }

                writer.Write((ushort)(color & 0x7FFF));
            }
        }

        return output.ToArray();
    }

    public bool SavePendingArtChanges(out string message)
    {
        message = string.Empty;

        if (!useUop)
        {
            message = "Saving pending art changes currently supports artLegacyMUL.uop only.";
            return false;
        }

        if (pendingUopArtEdits.Count == 0)
        {
            message = "No pending art changes to save.";
            return false;
        }

        if (artUopReader == null || !artUopReader.IsLoaded || string.IsNullOrWhiteSpace(artLegacyUopPath))
        {
            message = "artLegacyMUL.uop is not loaded.";
            return false;
        }

        try
        {
            Dictionary<ulong, UopDataHeader> existingHeaders = artUopReader.GetAllEntries();
            List<UopFileData> outputEntries = new();

            foreach (KeyValuePair<ulong, UopDataHeader> pair in existingHeaders)
            {
                if (pendingUopArtEdits.ContainsKey(pair.Key))
                {
                    continue;
                }

                byte[]? data = artUopReader.ReadData(pair.Value);
                if (data == null || data.Length == 0)
                {
                    continue;
                }

                outputEntries.Add(new UopFileData
                {
                    Hash = pair.Key,
                    Data = data,
                    DecompressedSize = (uint)data.Length,
                    IsCompressed = true,
                    IsEmpty = false
                });
            }

            outputEntries.AddRange(pendingUopArtEdits.Values);

            string backupPath = artLegacyUopPath + ".bak";
            if (!File.Exists(backupPath))
            {
                File.Copy(artLegacyUopPath, backupPath, false);
            }

            int savedCount = pendingUopArtEdits.Count;

            UopFileWriter.WriteUopFile(artLegacyUopPath, outputEntries, 1000);

            pendingUopArtEdits.Clear();
            Initialize(folderPath);

            message = "Saved " + savedCount + " art changes to artLegacyMUL.uop.";
            return true;
        }
        catch (Exception exception)
        {
            message = "Save art changes failed: " + exception.Message;
            return false;
        }
    }
}
