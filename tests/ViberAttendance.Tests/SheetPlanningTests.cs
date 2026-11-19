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
    public void NamesThatDifferOnlyBySpacesUseTheSameSheetRow()
    {
        IReadOnlyList<IReadOnlyList<string>> rows =
        [
            ["姓名", "上线时间", "下线时间", "当前状态", "累计时长"],
            ["张三", "", "", "", ""],
            ["", "", "", "", ""]
        ];
        var person = new DetailSheetPerson("id", "张 三", "2026-11-17 19:01:00", "", "在线", "10分钟");

        var plan = DetailSheetPlanner.Plan(rows, [person]);

        Assert.Contains(plan.Cells, cell => cell.Row == 2 && cell.Column == 0 && cell.Value == "张 三");
        Assert.DoesNotContain(plan.Cells, cell => cell.Row == 3);
    }

    [Fact]
    public void ReadRangeCoversAColumnPastZ()
    {
        var settings = new SheetSyncSettings { DetailNameColumn = "AA" };

        Assert.Equal(27, SheetColumns.ReadColumnCount(settings));
        Assert.Equal("AA", SheetColumns.Letter(26));
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
            DetailDurationColumn = "G"
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
        Assert.DoesNotContain(plan.Cells, cell => cell.Column is < 2 or > 6);
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
        Assert.Equal("10分钟", left.DurationText);

        var onlineRows = AttendanceSheetRows.BuildDetail(call,
        [
            Interval(call, member, "安娜", online, online.AddMinutes(1), "Online")
        ]);
        var present = Assert.Single(onlineRows);
        Assert.Equal("entrar en línea", present.Status);
        Assert.Equal(string.Empty, present.OfflineText);
    }

    [Fact]
    public void SamePersonUpdatesTheExistingNameAndAddsMinutes()
    {
        IReadOnlyList<IReadOnlyList<string>> rows =
        [
            ["姓名", "上线时间", "下线时间", "当前状态", "累计时长"],
            ["安娜", "旧上线", "旧下线", "desconectado", "3分钟"]
        ];
        var latest = new DetailSheetPerson("new", "安娜", "新上线", "", "entrar en línea", "10分钟");
        var older = new DetailSheetPerson("old", "安娜", "更早上线", "更早下线", "desconectado", "5分钟");

        var plan = DetailSheetPlanner.Plan(rows, [latest, older]);

        Assert.Contains(plan.Cells, cell => cell.Row == 2 && cell.Column == 3 && cell.Value == "entrar en línea");
        Assert.Contains(plan.Cells, cell => cell.Row == 2 && cell.Column == 4 && cell.Value == "15分钟");
        Assert.DoesNotContain(plan.Cells, cell => cell.Row == 3);
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

        Assert.Contains(plan.Cells, cell => cell.Row == 3 && cell.Column == 1 && cell.Value.Contains("45分钟", StringComparison.Ordinal) && cell.Value.Contains('●', StringComparison.Ordinal) && cell.Value.Contains("备注", StringComparison.Ordinal));
        Assert.Contains(plan.Cells, cell => cell.Row == 2 && cell.Column == 0 && cell.Value == "新人");
        Assert.Contains(plan.Cells, cell => cell.Row == 2 && cell.Column == 1 && cell.Value.Contains('★', StringComparison.Ordinal) && cell.Value.Contains("90分钟", StringComparison.Ordinal));
        Assert.DoesNotContain(plan.Cells, cell => cell.Column == 2);
        Assert.Equal(["放不下"], plan.OverflowNames);
        Assert.DoesNotContain(plan.Cells, cell => cell.Row == 4);
        Assert.Contains(plan.Colors, color => color.Row == 3 && color.Hex == "#F9A825");
        Assert.Contains(plan.Colors, color => color.Row == 2 && color.Hex == "#43A047");
    }

    [Fact]
    public void TimeCellKeepsTheTypedSymbolAndAddsPrefixBeforeTheDate()
    {
        var scheme = TimeMarkScheme.CreateDefault();
        scheme.DefaultSymbol = "甲";
        scheme.Tiers[0].Symbol = "长档";
        scheme.PrefixText = "优秀";
        scheme.PrefixMinimumMinutes = 60;
        scheme.Normalize();
        var longStay = AttendanceSheetRows.FormatTimeCell(Person("长", 120), scheme);
        var shortStay = AttendanceSheetRows.FormatTimeCell(Person("短", 20), scheme);

        Assert.Contains("长档", longStay, StringComparison.Ordinal);
        Assert.StartsWith("优秀 ", longStay, StringComparison.Ordinal);
        Assert.Contains("120分钟", longStay, StringComparison.Ordinal);
        Assert.Contains("备注", longStay, StringComparison.Ordinal);
        Assert.Contains("甲", shortStay, StringComparison.Ordinal);
        Assert.DoesNotContain("优秀", shortStay, StringComparison.Ordinal);
        Assert.Equal("长档", scheme.Resolve(TimeSpan.FromMinutes(120)).Symbol);
    }

    [Fact]
    public void ShorterThanEveryTierUsesTheDefaultMark()
    {
        var scheme = TimeMarkScheme.CreateDefault();
        var person = Person("短", 5);
        var text = AttendanceSheetRows.FormatTimeCell(person, scheme);

        Assert.Contains('○', text);
        Assert.Contains("5分钟", text, StringComparison.Ordinal);
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

    [Fact]
    public void CustomNumbersIgnoreSpacesAndCanBeLeftOutOfTheSheet()
    {
        var pasted = CustomNumberDirectory.Parse("姓名\t号码\r\n张 三\t18\r\n李四,27\r\n王五  36");
        var stored = CustomNumberDirectory.Merge(
            [new CustomNumber { Name = "张三", Code = "旧" }],
            pasted);
        var index = CustomNumberDirectory.Index(stored);

        Assert.Equal("18", CustomNumberDirectory.Find(index, "张三"));
        Assert.Equal("18", CustomNumberDirectory.Find(index, "张　三"));
        Assert.Equal("27", CustomNumberDirectory.Find(index, "李 四"));
        Assert.Equal("36", CustomNumberDirectory.Find(index, "王五"));
        Assert.Equal(string.Empty, CustomNumberDirectory.Find(index, "没有的人"));

        var settings = new SheetSyncSettings
        {
            DetailCodeEnabled = true,
            DetailCodeColumn = "F",
            CustomNumbers = stored
        };
        settings.Normalize();
        var person = new DetailSheetPerson("id", "张 三", "上线", "", "entrar en línea", "10分钟");
        var enabled = DetailSheetPlanner.Plan([], [person], DetailSheetColumns.From(settings), CustomNumberDirectory.Index(settings.CustomNumbers));
        Assert.Contains(enabled.Cells, cell => cell.Row == 2 && cell.Column == 5 && cell.Value == "18");
        Assert.Contains(enabled.Cells, cell => cell.Row == 1 && cell.Column == 5 && cell.Value == "自定义号码");

        settings.DetailCodeEnabled = false;
        var disabled = DetailSheetPlanner.Plan([], [person], DetailSheetColumns.From(settings), CustomNumberDirectory.Index(settings.CustomNumbers));
        Assert.DoesNotContain(disabled.Cells, cell => cell.Column == 5);
    }

    [Fact]
    public void FinalSheetWritesTheCustomNumberOnlyWhenAsked()
    {
        var settings = new SheetSyncSettings
        {
            NameColumn = "A",
            TimeColumn = "B",
            TimeCodeEnabled = true,
            TimeCodeColumn = "C",
            StartRow = 2,
            EndRow = 4,
            CustomNumbers = [new CustomNumber { Name = "安 娜", Code = "VB18" }]
        };
        settings.Normalize();
        var plan = TimeSheetPlanner.Plan([], settings, [Person("安娜", 30)]);

        Assert.Contains(plan.Cells, cell => cell.Row == 2 && cell.Column == 2 && cell.Value == "VB18");

        settings.TimeCodeEnabled = false;
        var hidden = TimeSheetPlanner.Plan([], settings, [Person("安娜", 30)]);
        Assert.DoesNotContain(hidden.Cells, cell => cell.Column == 2);
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
