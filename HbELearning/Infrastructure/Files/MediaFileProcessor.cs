// File: Infrastructure/Files/MediaFileProcessor.cs

namespace HbELearning.Infrastructure.Files;

/// <summary>Hiện thực IMediaFileProcessor bằng WebmDurationFixer.</summary>
public sealed class MediaFileProcessor : IMediaFileProcessor
{
    public bool PatchWebmDuration(string filePath, double durationMs) => WebmDurationFixer.Patch(filePath, durationMs);
}
