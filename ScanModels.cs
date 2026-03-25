namespace UltraTree;

public sealed class VolumeStats
{
    public required string Root { get; init; }
    public required long TotalBytes { get; init; }
    public required long FreeBytes { get; init; }
    public long UsedBytes => TotalBytes - FreeBytes;
    public double UsedPercent => TotalBytes <= 0 ? 0 : (double)UsedBytes / TotalBytes * 100.0;
}

public sealed class NodeMetrics
{
    public required string Path { get; init; }
    public required bool IsDir { get; init; }

    public long SizeBytes { get; set; }
    public long AllocatedBytes { get; set; }
    public long Items { get; set; }
    public long Files { get; set; }
    public long Folders { get; set; }

    public double PercentOfParent { get; set; }
}