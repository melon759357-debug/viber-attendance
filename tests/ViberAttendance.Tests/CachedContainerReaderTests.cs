using ViberAttendance.UIAutomation;

namespace ViberAttendance.Tests;

public sealed class CachedContainerReaderTests
{
    private sealed class Node(string label)
    {
        public string Label { get; } = label;
        public bool IsAlive { get; set; } = true;
    }

    [Fact]
    public void SecondReadUsesCachedContainerWithoutFullWalk()
    {
        var reader = new CachedContainerReader<Node, string>();
        var container = new Node("participants");
        var fullWalks = 0;

        var first = reader.Read(1, node => "cached:" + node.Label, () =>
        {
            fullWalks++;
            return (container, "full");
        });
        var second = reader.Read(1, node => "cached:" + node.Label, () =>
        {
            fullWalks++;
            return (container, "full");
        });

        Assert.Equal("full", first);
        Assert.Equal("cached:participants", second);
        Assert.Equal(1, fullWalks);
    }

    [Fact]
    public void StaleContainerFallsBackToFullWalkAndIsReplaced()
    {
        var reader = new CachedContainerReader<Node, string>();
        var stale = new Node("old");
        var fresh = new Node("new");
        reader.Read(1, _ => null, () => (stale, "full-1"));
        stale.IsAlive = false;

        var result = reader.Read(
            1,
            node => node.IsAlive ? "cached:" + node.Label : throw new InvalidOperationException("element gone"),
            () => (fresh, "full-2"));
        var next = reader.Read(1, node => "cached:" + node.Label, () => (null, "unexpected"));

        Assert.Equal("full-2", result);
        Assert.Equal("cached:new", next);
    }

    [Fact]
    public void EmptyCachedReadFallsBackAndWindowsAreIndependent()
    {
        var reader = new CachedContainerReader<Node, string>();
        reader.Read(1, _ => null, () => (new Node("main"), "main-full"));
        reader.Read(2, _ => null, () => (new Node("preview"), "preview-full"));
        Assert.Equal(2, reader.CachedWindowCount);

        var result = reader.Read(1, _ => null, () => (null, "main-full-again"));

        Assert.Equal("main-full-again", result);
        Assert.Equal(1, reader.CachedWindowCount);
        Assert.Equal("cached:preview", reader.Read(2, node => "cached:" + node.Label, () => (null, "unexpected")));
    }
}
