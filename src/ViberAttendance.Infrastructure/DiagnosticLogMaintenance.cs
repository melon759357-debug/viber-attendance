using ViberAttendance.Core.Contracts;

namespace ViberAttendance.Infrastructure;

public sealed class DiagnosticLogMaintenance(ApplicationPaths paths) : IDiagnosticLogMaintenance
{
    public Task<int> ClearAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => ClearCore(cancellationToken), cancellationToken);

    private int ClearCore(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(paths.LogsFolder))
        {
            return 0;
        }

        var deleted = 0;
        foreach (var file in Directory.EnumerateFiles(paths.LogsFolder, "diagnostic-*.jsonl", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            File.Delete(file);
            deleted++;
        }

        return deleted;
    }
}
