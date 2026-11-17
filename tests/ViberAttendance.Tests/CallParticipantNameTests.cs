using ViberAttendance.UIAutomation;

namespace ViberAttendance.Tests;

public sealed class CallParticipantNameTests
{
    private const string PreviewPrefix =
        "QGuiApplication.videoPreviewWindow.CallWebEngineView_QMLTYPE_857.QObject.container.participants";

    private const string MainPrefix =
        "QGuiApplication.MainWindow_QMLTYPE_80.CallWebEngineView_QMLTYPE_857.QObject.container.participants";

    [Fact]
    public void WebViewParticipantTextKeepsStableIdAcrossWindows()
    {
        const string id = "Wk3QzfmD7h4=";
        var preview = $"{PreviewPrefix}.parent-{id}.{id}.name-{id}.QObject.QObject";
        var main = $"{MainPrefix}.parent-{id}.{id}.name-{id}.QObject.QObject";

        Assert.True(CallParticipantNames.TryReadNameNode(preview, "\u2068mimovil\u2069", true, out var previewId, out var previewName));
        Assert.True(CallParticipantNames.TryReadNameNode(main, "\u2068mimovil\u2069", true, out var mainId, out var mainName));

        Assert.Equal(id, previewId);
        Assert.Equal(previewId, mainId);
        Assert.Equal("mimovil", previewName);
        Assert.Equal(previewName, mainName);
    }

    [Fact]
    public void SelfMarkerIsRemovedOnlyFromTheLocalUser()
    {
        var selfId = $"{PreviewPrefix}.parent-self.self.name-self.QObject.QObject";
        var otherId = $"{PreviewPrefix}.parent-abc.abc.name-abc.QObject.QObject";

        Assert.True(CallParticipantNames.TryReadNameNode(selfId, "\u2068Anna\u2069 (我)", true, out var stableId, out var displayName));
        Assert.Equal("self", stableId);
        Assert.Equal("Anna", displayName);

        Assert.True(CallParticipantNames.TryReadNameNode(otherId, "Boris (我)", true, out _, out var otherName));
        Assert.Equal("Boris (我)", otherName);
    }

    [Fact]
    public void SpotlightDuplicateAndAddButtonAreNotParticipants()
    {
        var spotlight = $"{MainPrefix}.parent-main.main.name-main.QObject.QObject";
        Assert.False(CallParticipantNames.TryReadNameNode(spotlight, "Anna", true, out _, out _));
        Assert.False(CallParticipantNames.IsParticipantContainer(
            "QGuiApplication.MainWindow.AddParticipantsButton_QMLTYPE_1875"));
        Assert.True(CallParticipantNames.IsParticipantContainer($"{MainPrefix}"));
        Assert.True(CallParticipantNames.IsCallSurface(
            "QGuiApplication.videoPreviewWindow",
            "CallPreviewWindow_QMLTYPE_777"));
    }
}
