using ViberAttendance.Core.Configuration;
using ViberAttendance.Core.Models;
using ViberAttendance.Core.Sheets;

namespace ViberAttendance.Tests;

public sealed class SheetPlanningTests
{
    [Fact]
    public void DetailSheetUsesEmptyRowAndUpdatesSameRecordLater()
    {
        IReadOnlyList<IReadOnlyList<string>> rows =
        [
            ["姓名", "上线时间", "下线时间", "当前状态", "累计时长", "记录编号"],
            ["", "", "", "", "", ""],
            ["别人", "x", "x", "x", "x", "keep"],
            ["", "", "", "", "", ""]
        ];
        var person = new DetailSheetPerson("call:member", "安娜", "2026-11-17 19:01:00", "", "在线", "00:10:00");

        var first = DetailSheetPlanner.Plan(rows, [person]);

        Assert.Contains(first.Cells, cell => cell.Row == 2 && cell.Column == 0 && cell.Value == "安娜");
        Assert.DoesNotContain(first.Cells, cell => cell.Row == 3 && cell.Column == 0);

        var updatedGrid = new List<IReadOnlyList<string>>
        {
            rows[0],
            new[] { "安娜", "2026-11-17 19:01:00", "", "在线", "00:10:00", "call:member" },
            rows[2],
            rows[3]
        };
        var revised = person with { Status = "已下线", OfflineText = "2026-11-17 19:40:00" };
        var second = DetailSheetPlanner.Plan(updatedGrid, [revised]);

        Assert.Contains(second.Cells, cell => cell.Row == 2 && cell.Column == 3 && cell.Value == "已下线");
        Assert.DoesNotContain(second.Cells, cell => cell.Row == 4);
    }

    [Fact]
    public void DetailSheetWritesIntoTheColumnsTheUserChose()
    {
        var settings = new SheetSyncSettings
        {
            DetailNameColumn = "C",
            DetailOnlineColumn = "D",
            DetailOfflineColumn = "E",
            DetailStatusColumn = "F",
            DetailDurationColumn = "G",
            DetailRecordColumn = "H"
        };
        settings.Normalize();
        IReadOnlyList<IReadOnlyList<string>> rows =
        [
            ["别动", "", "姓名", "上线时间", "下线时间", "当前状态", "累计时长", "记录编号"],
            ["", "", "", "", "", "", "", ""],
            ["别动", "", "别人", "x", "x", "x", "x", "keep"]
        ];
        var person = new DetailSheetPerson("call:member", "安娜", "2026-11-17 19:01:00", "", "在线", "00:10:00");

        var plan = DetailSheetPlanner.Plan(rows, [person], DetailSheetColumns.From(settings));

        Assert.Contains(plan.Cells, cell => cell.Row == 2 && cell.Column == 2 && cell.Value == "安娜");
        Assert.Contains(plan.Cells, cell => cell.Row == 2 && cell.Column == 7 && cell.Value == "call:member");
        Assert.DoesNotContain(plan.Cells, cell => cell.Column < 2);
        Assert.DoesNotContain(plan.Cells, cell => cell.Row == 3);
    }

    [Fact]
    public void DetailSheetWritesWholeMinutesAndSpanishStatus()
    {
        var call = Guid.NewGuid();
        var member = Guid.NewGuid();
        var online = new DateTimeOffset(2026, 11, 17, 18, 0, 0, TimeSpan.Zero);
        var offline = AttendanceSheetRows.BuildDetail(call,
        [
            Interval(call, member, "安娜", online, online.AddMinutes(10).AddSeconds(59), "Offline")
        ]);
        var left = Assert.Single(offline);
        Assert.Equal("desconectado", left.Status);
        Assert.Equal("10", left.DurationText);

        var onlineRows = AttendanceSheetRows.BuildDetail(call,
        [
            Interval(call, member, "安娜", online, online.AddMinutes(1), "Online")
        ]);
        var present = Assert.Single(onlineRows);
        Assert.Equal("entrar en línea", present.Status);
        Assert.Equal(string.Empty, present.OfflineText);
    }

    [Fact]
    public void TimeSheetStaysInsideTheTopRangeAndMatchesDurationTiers()
    {
        var settings = new SheetSyncSettings
        {
            NameColumn = "A",
            TimeColumn = "B",
            NoteColumn = "C",
            StartRow = 2,
            EndRow = 3,
            ActiveSchemeName = "默认",
            Schemes = [TimeMarkScheme.CreateDefault()]
        };
        settings.Normalize();
        IReadOnlyList<IReadOnlyList<string>> rows =
        [
            ["表头", "时间", "备注"],
            ["", "", ""],
            ["已有的人", "旧内容", "旧备注"],
            ["下面的内容", "不要碰", "不要碰"]
        ];
        var known = Person("已有的人", 45);
        var newbie = Person("新人", 90);
        var overflow = Person("放不下", 5);

        var plan = TimeSheetPlanner.Plan(rows, settings, [known, newbie, overflow]);

        Assert.Contains(plan.Cells, cell => cell.Row == 3 && cell.Column == 1 && cell.Value.Contains('●', StringComparison.Ordinal) && cell.Value.EndsWith(" VB", StringComparison.Ordinal));
        Assert.Contains(plan.Cells, cell => cell.Row == 2 && cell.Column == 0 && cell.Value == "新人");
        Assert.Contains(plan.Cells, cell => cell.Row == 2 && cell.Column == 1 && cell.Value.Contains('★', StringComparison.Ordinal));
        Assert.Equal(["放不下"], plan.OverflowNames);
        Assert.DoesNotContain(plan.Cells, cell => cell.Row == 4);
        Assert.Contains(plan.Colors, color => color.Row == 3 && color.Hex == "#F9A825");
        Assert.Contains(plan.Colors, color => color.Row == 2 && color.Hex == "#43A047");
    }

    [Fact]
    public void ShorterThanEveryTierUsesTheDefaultMark()
    {
        var scheme = TimeMarkScheme.CreateDefault();
        var person = Person("短", 5);
        var text = AttendanceSheetRows.FormatTimeCell(person, scheme);

        Assert.Contains('○', text);
        Assert.Equal("○", scheme.Resolve(TimeSpan.FromMinutes(5)).Symbol);
        Assert.Equal("#E53935", scheme.Resolve(TimeSpan.FromMinutes(5)).Color);
    }

    [Fact]
    public void NoteListsEachSegmentAndInterruptedEnding()
    {
        var member = Guid.NewGuid();
        var call = Guid.NewGuid();
        var first = new DateTimeOffset(2026, 11, 17, 18, 1, 0, TimeSpan.Zero);
        var second = new DateTimeOffset(2026, 11, 17, 18, 55, 0, TimeSpan.Zero);
        var rows = AttendanceSheetRows.BuildTime(
        [
            Interval(call, member, "安娜", first, first.AddMinutes(39), "Offline"),
            Interval(call, member, "安娜", second, second.AddMinutes(10), UserFacingStatuses.InterruptedStatus)
        ]);

        var note = Assert.Single(rows).Note;
        Assert.Contains("上线 ", note, StringComparison.Ordinal);
        Assert.Contains("程序中断", note, StringComparison.Ordinal);
        Assert.Contains('；', note);
    }

    private static TimeSheetPerson Person(string name, int minutes) =>
        new(name, new DateTimeOffset(2026, 11, 17, 19, 0, 0, TimeSpan.FromHours(1)), TimeSpan.FromMinutes(minutes), "备注");

    private static AttendanceIntervalRecord Interval(
        Guid callId,
        Guid memberId,
        string name,
        DateTimeOffset online,
        DateTimeOffset offline,
        string status) =>
        new(Guid.NewGuid(), callId, memberId, name, online, online, offline, offline, offline - online, status);
}
