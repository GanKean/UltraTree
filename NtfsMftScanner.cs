using Microsoft.Win32.SafeHandles;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace UltraTree;

/// <summary>
/// NTFS $MFT-based scanner (WizTree-style).
/// Reads raw volume, parses MFT records, extracts names + parent record + logical sizes.
/// Then resolves full paths and computes Allocated bytes via Win32.
/// </summary>
public static class NtfsMftScanner
{
    public sealed class ScanResult
    {
        // Drive summary (total/free/used)
        public required VolumeStats Volume { get; init; }

        // WizTree-style metrics (folder nodes have % of parent, allocated, etc.)
        public required IReadOnlyDictionary<string, NodeMetrics> Metrics { get; init; }

        // Convenience lists
        public long FileCount { get; init; }
        public long TotalBytes { get; init; }
        public List<ResultRow> TopFiles { get; init; } = new();
        public List<ResultRow> TopFolders { get; init; } = new();
    }

    public static ScanResult ScanDrive(string driveLetterNoSlash, IProgress<ScanProgress>? progress, CancellationToken ct)
    {
        string drive = driveLetterNoSlash.TrimEnd('\\');     // "C:"
        string rootPath = drive + "\\";                      // "C:\"
        var volumeStats = VolumeInfo.GetVolumeStats(rootPath);

        string volPath = @"\\.\" + drive;

        using SafeFileHandle hVol = CreateFileW(
            volPath,
            FileAccessFlags.GENERIC_READ,
            FileShareFlags.FILE_SHARE_READ | FileShareFlags.FILE_SHARE_WRITE | FileShareFlags.FILE_SHARE_DELETE,
            IntPtr.Zero,
            CreationDisposition.OPEN_EXISTING,
            FileFlagsAndAttributes.FILE_ATTRIBUTE_NORMAL,
            IntPtr.Zero);

        if (hVol.IsInvalid)
            throw new InvalidOperationException("Cannot open raw volume handle. Run as Administrator and ensure this is NTFS.");

        // Read NTFS boot sector (first 512 bytes)
        byte[] boot = new byte[512];
        ReadAt(hVol, 0, boot);

        var bs = ParseBootSector(boot);
        if (!bs.IsNtfs)
            throw new InvalidOperationException("This volume does not appear to be NTFS.");

        progress?.Report(new ScanProgress(3, $"NTFS detected. MFT @ cluster {bs.MftCluster}, record size {bs.MftRecordSize} bytes"));

        long bytesPerCluster = bs.BytesPerSector * bs.SectorsPerCluster;
        long mftByteOffset = bs.MftCluster * bytesPerCluster;

        // Store record-index keyed entries (record number == frn for our purposes)
        var entries = new ConcurrentDictionary<ulong, Entry>(concurrencyLevel: Environment.ProcessorCount, capacity: 1_000_000);

        // Root record is usually 5 on NTFS
        const ulong ROOT_RECORD = 5;

        // Seed root as a directory (name can be empty; path cache handles full root path)
        entries[ROOT_RECORD] = new Entry(ROOT_RECORD, ROOT_RECORD, "", IsDir: true, Size: 0);

        // Scan MFT records sequentially
        int recordSize = bs.MftRecordSize;
        byte[] recordBuf = new byte[recordSize];

        long recordIndex = 0;
        int invalidStreak = 0;
        const int INVALID_STREAK_STOP = 200_000;

        long totalBytes = 0;
        long fileCount = 0;

        progress?.Report(new ScanProgress(5, "Parsing $MFT records…"));

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            long offset = mftByteOffset + recordIndex * recordSize;
            if (!TryReadAt(hVol, offset, recordBuf))
                break;

            if (!IsFileRecord(recordBuf))
            {
                recordIndex++;
                invalidStreak++;
                if (invalidStreak > INVALID_STREAK_STOP && recordIndex > 500_000)
                    break;
                continue;
            }

            invalidStreak = 0;

            if (!ApplyUsaFixup(recordBuf, bs.BytesPerSector))
            {
                recordIndex++;
                continue;
            }

            if (!TryParseRecord(recordBuf, out var rec))
            {
                recordIndex++;
                continue;
            }

            ulong frn = (ulong)recordIndex;

            // Save entry by record number
            if (!string.IsNullOrEmpty(rec.Name))
            {
                entries[frn] = new Entry(frn, rec.ParentFrn, rec.Name, rec.IsDir, rec.DataSize);

                if (!rec.IsDir && rec.DataSize > 0)
                {
                    Interlocked.Add(ref totalBytes, rec.DataSize);
                    Interlocked.Increment(ref fileCount);
                }
            }

            if (recordIndex % 200_000 == 0 && recordIndex > 0)
                progress?.Report(new ScanProgress(20, $"Parsed {recordIndex:n0} MFT records…"));

            recordIndex++;
        }

        progress?.Report(new ScanProgress(45, "Resolving paths…"));

        // Path reconstruction with memoization (record -> full path)
        var pathCache = new ConcurrentDictionary<ulong, string>(concurrencyLevel: Environment.ProcessorCount, capacity: 300_000);
        pathCache[ROOT_RECORD] = rootPath;

        string ResolvePath(ulong frn)
        {
            if (pathCache.TryGetValue(frn, out var cached))
                return cached;

            if (!entries.TryGetValue(frn, out var e))
                return "";

            if (e.ParentFrn == frn || frn == ROOT_RECORD)
            {
                pathCache[frn] = rootPath;
                return rootPath;
            }

            string parent = ResolvePath(e.ParentFrn);
            if (string.IsNullOrEmpty(parent))
                return "";

            string full = Path.Combine(parent, e.Name);
            if (e.IsDir && !full.EndsWith("\\")) full += "\\";
            pathCache[frn] = full;
            return full;
        }

        progress?.Report(new ScanProgress(60, "Computing allocated size + building metrics…"));

        // Build FileFacts (for MetricsBuilder)
        var factsBag = new ConcurrentBag<FileFact>();

        // Add directory facts (optional but helps folder counts)
        foreach (var kv in entries)
        {
            ct.ThrowIfCancellationRequested();

            var e = kv.Value;
            if (!e.IsDir) continue;

            var p = ResolvePath(e.Frn);
            if (string.IsNullOrEmpty(p)) continue;
            if (!p.EndsWith("\\")) p += "\\";

            string parent = Path.GetDirectoryName(p.TrimEnd('\\')) ?? rootPath.TrimEnd('\\');
            if (!parent.EndsWith("\\")) parent += "\\";

            factsBag.Add(new FileFact
            {
                Path = p,
                ParentPath = parent,
                IsDir = true,
                LogicalSize = 0,
                AllocatedSize = 0
            });
        }

        // Top heaps built from resolved facts
        var topFilesHeap = new FixedSizeMinHeap(1000);

        // Process files in parallel: resolve path, compute allocated, add facts
        var fileEntries = entries.Values.Where(x => !x.IsDir && x.Size > 0).ToArray();

        Parallel.ForEach(
            fileEntries,
            new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = Environment.ProcessorCount },
            e =>
            {
                ct.ThrowIfCancellationRequested();

                string full = ResolvePath(e.Frn);
                if (string.IsNullOrEmpty(full))
                    return;

                long alloc;
                try
                {
                    alloc = AllocatedSize.GetAllocatedBytesForFile(full);
                    if (alloc < 0) alloc = e.Size;
                }
                catch
                {
                    alloc = e.Size; // fallback
                }

                string parent = Path.GetDirectoryName(full) ?? rootPath.TrimEnd('\\');
                if (!parent.EndsWith("\\")) parent += "\\";

                factsBag.Add(new FileFact
                {
                    Path = full,
                    ParentPath = parent,
                    IsDir = false,
                    LogicalSize = e.Size,
                    AllocatedSize = alloc
                });

                topFilesHeap.Consider(full, e.Size);
            });

        // Build WizTree-style metrics tree (includes % of parent)
        var metrics = MetricsBuilder.BuildTreeMetrics(factsBag, rootPath, ct);

        // Build top folders from metrics
        var topFoldersHeap = new FixedSizeMinHeap(500);
        foreach (var n in metrics.Values)
        {
            ct.ThrowIfCancellationRequested();
            if (!n.IsDir) continue;

            // skip root
            if (n.Path.TrimEnd('\\').Equals(rootPath.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                continue;

            topFoldersHeap.Consider(n.Path, n.SizeBytes);
        }

        progress?.Report(new ScanProgress(95, "Finalizing…"));

        return new ScanResult
        {
            Volume = volumeStats,
            Metrics = metrics,

            FileCount = fileCount,
            TotalBytes = totalBytes,

            TopFiles = topFilesHeap.GetTopDescending(200).Select(x => new ResultRow(x.Path, x.Bytes)).ToList(),
            TopFolders = topFoldersHeap.GetTopDescending(200).Select(x => new ResultRow(x.Path, x.Bytes)).ToList()
        };
    }

    // -------------------- Parsing --------------------

    private static BootSector ParseBootSector(ReadOnlySpan<byte> boot)
    {
        bool isNtfs = boot.Length >= 11 &&
                      boot[3] == (byte)'N' && boot[4] == (byte)'T' && boot[5] == (byte)'F' && boot[6] == (byte)'S';

        ushort bytesPerSector = BinaryPrimitives.ReadUInt16LittleEndian(boot.Slice(0x0B, 2));
        byte sectorsPerCluster = boot[0x0D];

        long mftCluster = BinaryPrimitives.ReadInt64LittleEndian(boot.Slice(0x30, 8));

        sbyte clustersPerFileRecord = unchecked((sbyte)boot[0x40]);
        int recordSize;
        if (clustersPerFileRecord < 0)
            recordSize = 1 << (-clustersPerFileRecord);
        else
            recordSize = (int)(clustersPerFileRecord * (long)bytesPerSector * sectorsPerCluster);

        return new BootSector(isNtfs, bytesPerSector, sectorsPerCluster, mftCluster, recordSize);
    }

    private static bool IsFileRecord(ReadOnlySpan<byte> record)
        => record.Length >= 4 &&
           record[0] == (byte)'F' &&
           record[1] == (byte)'I' &&
           record[2] == (byte)'L' &&
           record[3] == (byte)'E';

    private static bool ApplyUsaFixup(Span<byte> record, int bytesPerSector)
    {
        if (record.Length < 0x08) return false;

        ushort usaOffset = BinaryPrimitives.ReadUInt16LittleEndian(record.Slice(0x04, 2));
        ushort usaCount = BinaryPrimitives.ReadUInt16LittleEndian(record.Slice(0x06, 2));

        if (usaOffset + usaCount * 2 > record.Length) return false;
        if (usaCount < 2) return false;

        ushort usaSeq = BinaryPrimitives.ReadUInt16LittleEndian(record.Slice(usaOffset, 2));

        for (int i = 1; i < usaCount; i++)
        {
            int sectorEnd = i * bytesPerSector - 2;
            if (sectorEnd < 0 || sectorEnd + 2 > record.Length) return false;

            ushort current = BinaryPrimitives.ReadUInt16LittleEndian(record.Slice(sectorEnd, 2));
            if (current != usaSeq)
                return false;

            ushort replacement = BinaryPrimitives.ReadUInt16LittleEndian(record.Slice(usaOffset + i * 2, 2));
            BinaryPrimitives.WriteUInt16LittleEndian(record.Slice(sectorEnd, 2), replacement);
        }

        return true;
    }

    private static bool TryParseRecord(ReadOnlySpan<byte> record, out ParsedRecord parsed)
    {
        parsed = default;

        if (record.Length < 0x30) return false;

        ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(record.Slice(0x16, 2));
        bool inUse = (flags & 0x0001) != 0;
        bool isDir = (flags & 0x0002) != 0;
        if (!inUse) return false;

        ushort attrOffset = BinaryPrimitives.ReadUInt16LittleEndian(record.Slice(0x14, 2));
        if (attrOffset >= record.Length) return false;

        string bestName = "";
        ulong parentFrn = 0;
        byte bestNamespace = 255;

        long dataSize = 0;

        int pos = attrOffset;
        while (pos + 8 <= record.Length)
        {
            uint type = BinaryPrimitives.ReadUInt32LittleEndian(record.Slice(pos, 4));
            if (type == 0xFFFFFFFF) break;

            uint len = BinaryPrimitives.ReadUInt32LittleEndian(record.Slice(pos + 4, 4));
            if (len == 0 || pos + len > record.Length) break;

            byte nonResident = record[pos + 8];

            if (type == 0x30 && nonResident == 0) // FILE_NAME (resident)
            {
                uint vlen = BinaryPrimitives.ReadUInt32LittleEndian(record.Slice(pos + 16, 4));
                ushort voff = BinaryPrimitives.ReadUInt16LittleEndian(record.Slice(pos + 20, 2));
                int vpos = pos + voff;

                if (vpos + (int)vlen <= record.Length && vlen >= 0x42)
                {
                    ulong parent = BinaryPrimitives.ReadUInt64LittleEndian(record.Slice(vpos, 8)) & 0x0000FFFFFFFFFFFFUL;

                    byte nameLen = record[vpos + 0x40];
                    byte nameNs = record[vpos + 0x41];
                    int namePos = vpos + 0x42;
                    int nameBytes = nameLen * 2;

                    if (namePos + nameBytes <= record.Length)
                    {
                        string name = System.Text.Encoding.Unicode.GetString(record.Slice(namePos, nameBytes));
                        if (IsBetterNamespace(nameNs, bestNamespace))
                        {
                            bestNamespace = nameNs;
                            bestName = name;
                            parentFrn = parent;
                        }
                    }
                }
            }
            else if (type == 0x80) // DATA
            {
                if (nonResident == 0)
                {
                    uint vlen = BinaryPrimitives.ReadUInt32LittleEndian(record.Slice(pos + 16, 4));
                    dataSize = vlen;
                }
                else
                {
                    long realSize = BinaryPrimitives.ReadInt64LittleEndian(record.Slice(pos + 48, 8));
                    if (realSize > dataSize)
                        dataSize = realSize;
                }
            }

            pos += (int)len;
        }

        if (string.IsNullOrEmpty(bestName))
            return false;

        parsed = new ParsedRecord(bestName, parentFrn, isDir, dataSize);
        return true;
    }

    private static bool IsBetterNamespace(byte candidate, byte currentBest)
    {
        int Rank(byte ns) => ns switch
        {
            1 => 0, // Win32
            3 => 1, // Win32&DOS
            0 => 2, // POSIX
            2 => 3, // DOS
            _ => 4
        };

        return Rank(candidate) < Rank(currentBest);
    }

    private sealed record BootSector(bool IsNtfs, ushort BytesPerSector, byte SectorsPerCluster, long MftCluster, int MftRecordSize);
    private sealed record ParsedRecord(string Name, ulong ParentFrn, bool IsDir, long DataSize);
    private sealed record Entry(ulong Frn, ulong ParentFrn, string Name, bool IsDir, long Size);

    // -------------------- Raw IO helpers --------------------

    private static void ReadAt(SafeFileHandle h, long offset, byte[] buffer)
    {
        if (!SetFilePointerEx(h, offset, out _, SeekOriginFlags.Begin))
            throw new IOException("SetFilePointerEx failed.");

        if (!ReadFile(h, buffer, buffer.Length, out int read, IntPtr.Zero) || read != buffer.Length)
            throw new IOException("ReadFile failed.");
    }

    private static bool TryReadAt(SafeFileHandle h, long offset, byte[] buffer)
    {
        if (!SetFilePointerEx(h, offset, out _, SeekOriginFlags.Begin))
            return false;

        return ReadFile(h, buffer, buffer.Length, out int read, IntPtr.Zero) && read == buffer.Length;
    }

    // -------------------- P/Invoke --------------------

    [Flags]
    private enum FileAccessFlags : uint
    {
        GENERIC_READ = 0x80000000
    }

    [Flags]
    private enum FileShareFlags : uint
    {
        FILE_SHARE_READ = 0x00000001,
        FILE_SHARE_WRITE = 0x00000002,
        FILE_SHARE_DELETE = 0x00000004
    }

    private enum CreationDisposition : uint
    {
        OPEN_EXISTING = 3
    }

    [Flags]
    private enum FileFlagsAndAttributes : uint
    {
        FILE_ATTRIBUTE_NORMAL = 0x00000080
    }

    private enum SeekOriginFlags : uint
    {
        Begin = 0
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileW(
        string lpFileName,
        FileAccessFlags dwDesiredAccess,
        FileShareFlags dwShareMode,
        IntPtr lpSecurityAttributes,
        CreationDisposition dwCreationDisposition,
        FileFlagsAndAttributes dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetFilePointerEx(
        SafeFileHandle hFile,
        long liDistanceToMove,
        out long lpNewFilePointer,
        SeekOriginFlags dwMoveMethod);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadFile(
        SafeFileHandle hFile,
        byte[] lpBuffer,
        int nNumberOfBytesToRead,
        out int lpNumberOfBytesRead,
        IntPtr lpOverlapped);
}
