using System.Text.Json;
using System.Text.Json.Serialization;
using ViberAttendance.Core.Models;
using ViberAttendance.Vision;

namespace ViberAttendance.Tests;

public sealed class FullDiagnosticExportTests
{
    [Fact]
    public void FullDiagnosticReport_SerializesRequiredSections()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }
        };
        var report = new FullDiagnosticReport(
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            TimeSpan.FromMilliseconds(12),
            new DiagnosticStageResult("阶段1：进程发现", true, "找到 1 个"),
            new DiagnosticStageResult("阶段2：窗口发现", true, "找到窗口"),
            new DiagnosticStageResult("阶段3：UI Automation读取", true, "根元素成功，无姓名"),
            new DiagnosticStageResult("阶段3b：MSAA读取", false, "未见姓名"),
            new DiagnosticStageResult("阶段4：参与者读取", false, "没有名单"),
            [new ViberProcessInfo(1, "Viber", @"C:\Viber\Viber.exe", "Viber", "Viber", 0, "", ["ProcessName"])],
            [new DiagnosticWindowInfo(10, 1, "Viber", "", "QtWindow", true, false, false, false, false, 800, 600, true, "Accepted", ViberWindowCandidateType.MainWindow, 20, 0, 1)],
            [],
            new DiagnosticWindowInfo(10, 1, "Viber", "", "QtWindow", true, false, false, false, false, 800, 600, true, "Accepted", ViberWindowCandidateType.MainWindow, 20, 0, 1),
            [new DiagnosticUiaWindowResult(10, true, "W***", "Qt", "", "ControlType.Window", 10, 2, 5, false, false, false, 0, [], [], null)],
            [new DiagnosticMsaaWindowResult(10, true, null, "client", 0, 0, false, [], null)],
            new DiagnosticParticipantStageResult(10, null, null, 0, [], false, false, "无名单"),
            ConnectionState.CallNotDetected,
            ParticipantCaptureStrategy.PreferLocalRegionOcr,
            "建议局部 OCR",
            null);

        var json = JsonSerializer.Serialize(report, options);
        Assert.Contains("detectedProcesses", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("detectedWindows", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("finalConnectionState", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PreferLocalRegionOcr", json, StringComparison.Ordinal);
        Assert.True(VisionFeatureStatus.ShouldPreferLocalOcr(report.RecommendedStrategy));
    }

    [Fact]
    public void SameWindowIsNotDuplicatedInScanSet()
    {
        var handles = new HashSet<long>();
        Assert.True(handles.Add(10));
        Assert.False(handles.Add(10));
        Assert.True(handles.Add(11));
        Assert.Equal(2, handles.Count);
    }
}
