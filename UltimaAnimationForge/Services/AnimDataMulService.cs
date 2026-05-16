using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UltimaAnimationForge.Models;

namespace UltimaAnimationForge.Services;

public sealed class AnimDataMulService
{
    private const int FramesPerEntry = 64;
    private const int EntriesPerChunk = 8;
    private const int EntryByteSize = FramesPerEntry + 4;
    private const int ChunkByteSize = 4 + (EntriesPerChunk * EntryByteSize);

    private int[] chunkHeaders = Array.Empty<int>();
    private byte[] tailBytes = Array.Empty<byte>();
    private List<AnimDataEntry> allLoadedEntries = new();

    public string FilePath { get; private set; } = string.Empty;
    public bool IsLoaded { get; private set; }

    public IReadOnlyList<AnimDataEntry> AllLoadedEntries => allLoadedEntries;

    public bool Initialize(string folderPath)
    {
        FilePath = string.Empty;
        IsLoaded = false;
        chunkHeaders = Array.Empty<int>();
        tailBytes = Array.Empty<byte>();
        allLoadedEntries = new List<AnimDataEntry>();

        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
        {
            return false;
        }

        string path = Path.Combine(folderPath, "animdata.mul");
        if (!File.Exists(path))
        {
            return false;
        }

        FilePath = path;
        allLoadedEntries = Load(path);
        IsLoaded = true;
        return true;
    }

    public List<AnimDataEntry> Load(string filePath)
    {
        List<AnimDataEntry> results = new();

        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return results;
        }

        using FileStream fileStream = new(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using BinaryReader reader = new(fileStream);

        int chunkCount = (int)(fileStream.Length / ChunkByteSize);
        chunkHeaders = new int[chunkCount];

        int id = 0;

        for (int chunkIndex = 0; chunkIndex < chunkCount; chunkIndex++)
        {
            chunkHeaders[chunkIndex] = reader.ReadInt32();

            for (int entryIndex = 0; entryIndex < EntriesPerChunk; entryIndex++, id++)
            {
                sbyte[] frameOffsets = new sbyte[FramesPerEntry];

                for (int frameIndex = 0; frameIndex < FramesPerEntry; frameIndex++)
                {
                    frameOffsets[frameIndex] = reader.ReadSByte();
                }

                byte unknown = reader.ReadByte();
                byte frameCount = reader.ReadByte();
                byte frameInterval = reader.ReadByte();
                byte frameStart = reader.ReadByte();

                if (frameCount == 0)
                {
                    continue;
                }

                if (frameCount > 64)
                {
                    frameCount = 64;
                }

                results.Add(new AnimDataEntry
                {
                    Id = id,
                    FrameOffsets = frameOffsets,
                    Unknown = unknown,
                    FrameCount = frameCount,
                    FrameInterval = frameInterval,
                    FrameStart = frameStart
                });
            }
        }

        int remaining = (int)(fileStream.Length - fileStream.Position);
        tailBytes = remaining > 0 ? reader.ReadBytes(remaining) : Array.Empty<byte>();

        return results;
    }

    public void Save(string folderPath, IEnumerable<AnimDataEntry> changedEntries)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            throw new ArgumentException("Folder path is required.", nameof(folderPath));
        }

        string path = Path.Combine(folderPath, "animdata.mul");
        SaveToFile(path, changedEntries);
    }

    public void SaveToFile(string filePath, IEnumerable<AnimDataEntry> changedEntries)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("File path is required.", nameof(filePath));
        }

        Dictionary<int, AnimDataEntry> entriesById = changedEntries
            .Where(entry => entry != null && entry.Id >= 0)
            .ToDictionary(entry => entry.Id, entry => entry);

        int maxId = entriesById.Count == 0 ? -1 : entriesById.Keys.Max();
        int chunkCount = Math.Max(chunkHeaders.Length, (maxId / EntriesPerChunk) + 1);

        using FileStream fileStream = new(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        using BinaryWriter writer = new(fileStream);

        int id = 0;

        for (int chunkIndex = 0; chunkIndex < chunkCount; chunkIndex++)
        {
            int header = chunkIndex < chunkHeaders.Length
                ? chunkHeaders[chunkIndex]
                : 0;

            writer.Write(header);

            for (int entryIndex = 0; entryIndex < EntriesPerChunk; entryIndex++, id++)
            {
                if (entriesById.TryGetValue(id, out AnimDataEntry? entry))
                {
                    WriteEntry(writer, entry);
                }
                else
                {
                    WriteEmptyEntry(writer);
                }
            }
        }

        if (tailBytes.Length > 0)
        {
            writer.Write(tailBytes);
        }
    }

    private static void WriteEntry(BinaryWriter writer, AnimDataEntry entry)
    {
        for (int frameIndex = 0; frameIndex < FramesPerEntry; frameIndex++)
        {
            sbyte value = 0;

            if (entry.FrameOffsets != null && frameIndex < entry.FrameOffsets.Length)
            {
                value = entry.FrameOffsets[frameIndex];
            }

            writer.Write(value);
        }

        writer.Write(entry.Unknown);
        writer.Write((byte)Math.Min(entry.FrameCount, (byte)64));
        writer.Write(entry.FrameInterval);
        writer.Write(entry.FrameStart);
    }

    private static void WriteEmptyEntry(BinaryWriter writer)
    {
        for (int frameIndex = 0; frameIndex < FramesPerEntry; frameIndex++)
        {
            writer.Write((sbyte)0);
        }

        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((byte)0);
    }

    public void AddOrReplaceEntry(AnimDataEntry entry)
    {
        if (entry == null || entry.Id < 0)
        {
            return;
        }

        int existingIndex = allLoadedEntries.FindIndex(x => x.Id == entry.Id);

        if (existingIndex >= 0)
        {
            allLoadedEntries[existingIndex] = entry;
        }
        else
        {
            allLoadedEntries.Add(entry);
            allLoadedEntries = allLoadedEntries
                .OrderBy(x => x.Id)
                .ToList();
        }
    }
}
