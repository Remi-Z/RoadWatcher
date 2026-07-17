using RoadWatcher.App;

namespace RoadWatcher.Tests;

public sealed class WorkbenchLayoutTests
{
    [Fact]
    public void MoveInto_SwapsTheExistingStablePaneHosts()
    {
        var layout = WorkbenchLayout.Default;

        var moved = layout.MoveInto(WorkbenchPane.Timeline, WorkbenchSlot.Primary);

        Assert.Equal(WorkbenchPane.Timeline, moved.PrimaryPane);
        Assert.Equal(WorkbenchPane.Map, moved.SecondaryPane);
        Assert.Equal(WorkbenchPane.Inspector, moved.SidePane);
        Assert.Equal(WorkbenchPane.Video, moved.BottomPane);
    }

    [Fact]
    public void Normalize_RejectsDuplicateOrUnknownAssignments()
    {
        var duplicate = new WorkbenchLayout
        {
            PrimaryPane = WorkbenchPane.Video,
            SecondaryPane = WorkbenchPane.Video,
            SidePane = WorkbenchPane.Inspector,
            BottomPane = WorkbenchPane.Timeline
        };
        var unknown = new WorkbenchLayout
        {
            PrimaryPane = (WorkbenchPane)42,
            SecondaryPane = WorkbenchPane.Map,
            SidePane = WorkbenchPane.Inspector,
            BottomPane = WorkbenchPane.Timeline
        };

        Assert.Equal(WorkbenchLayout.Default, WorkbenchLayout.Normalize(duplicate));
        Assert.Equal(WorkbenchLayout.Default, WorkbenchLayout.Normalize(unknown));
    }

    [Fact]
    public void PlacementFor_UsesFixedGridSlotsThatDoNotRequireReparenting()
    {
        Assert.Equal(new WorkbenchPanePlacement(1, 1, 1, 1), WorkbenchLayout.PlacementFor(WorkbenchSlot.Primary));
        Assert.Equal(new WorkbenchPanePlacement(1, 5, 3, 1), WorkbenchLayout.PlacementFor(WorkbenchSlot.Side));
        Assert.Equal(new WorkbenchPanePlacement(3, 1, 1, 3), WorkbenchLayout.PlacementFor(WorkbenchSlot.Bottom));
    }
}
