namespace RoadWatcher.App;

/// <summary>
/// The four stable workbench surfaces that can be arranged by the reviewer.
/// The pane instances themselves are never reparented, which keeps the native
/// LibVLC surface attached while the workspace is rearranged.
/// </summary>
public enum WorkbenchPane
{
    Video,
    Map,
    Inspector,
    Timeline
}

public enum WorkbenchSlot
{
    Primary,
    Secondary,
    Side,
    Bottom
}

public enum ApplicationThemeMode
{
    Dark,
    Light,
    System
}

public readonly record struct WorkbenchPanePlacement(int Row, int Column, int RowSpan, int ColumnSpan);

/// <summary>
/// App-local pane-to-slot assignments. Slots are deliberately fixed grid
/// regions: changing an assignment moves an existing host rather than moving
/// native controls through a visual-tree reparenting operation.
/// </summary>
public sealed record WorkbenchLayout
{
    public WorkbenchPane PrimaryPane { get; init; } = WorkbenchPane.Video;
    public WorkbenchPane SecondaryPane { get; init; } = WorkbenchPane.Map;
    public WorkbenchPane SidePane { get; init; } = WorkbenchPane.Inspector;
    public WorkbenchPane BottomPane { get; init; } = WorkbenchPane.Timeline;

    public static WorkbenchLayout Default { get; } = new();

    public static WorkbenchLayout Normalize(WorkbenchLayout? layout)
    {
        if (layout is null)
        {
            return Default;
        }

        var panes = new[] { layout.PrimaryPane, layout.SecondaryPane, layout.SidePane, layout.BottomPane };
        return panes.All(Enum.IsDefined) && panes.Distinct().Count() == Enum.GetValues<WorkbenchPane>().Length
            ? layout
            : Default;
    }

    public WorkbenchPane PaneAt(WorkbenchSlot slot) => slot switch
    {
        WorkbenchSlot.Primary => PrimaryPane,
        WorkbenchSlot.Secondary => SecondaryPane,
        WorkbenchSlot.Side => SidePane,
        WorkbenchSlot.Bottom => BottomPane,
        _ => throw new ArgumentOutOfRangeException(nameof(slot), slot, null)
    };

    public WorkbenchSlot SlotOf(WorkbenchPane pane)
    {
        var normalized = Normalize(this);
        if (normalized.PrimaryPane == pane) return WorkbenchSlot.Primary;
        if (normalized.SecondaryPane == pane) return WorkbenchSlot.Secondary;
        if (normalized.SidePane == pane) return WorkbenchSlot.Side;
        if (normalized.BottomPane == pane) return WorkbenchSlot.Bottom;
        throw new ArgumentOutOfRangeException(nameof(pane), pane, null);
    }

    public WorkbenchLayout MoveInto(WorkbenchPane pane, WorkbenchSlot destination)
    {
        var normalized = Normalize(this);
        var origin = normalized.SlotOf(pane);
        if (origin == destination)
        {
            return normalized;
        }

        var displaced = normalized.PaneAt(destination);
        return normalized with
        {
            PrimaryPane = destination == WorkbenchSlot.Primary
                ? pane
                : origin == WorkbenchSlot.Primary ? displaced : normalized.PrimaryPane,
            SecondaryPane = destination == WorkbenchSlot.Secondary
                ? pane
                : origin == WorkbenchSlot.Secondary ? displaced : normalized.SecondaryPane,
            SidePane = destination == WorkbenchSlot.Side
                ? pane
                : origin == WorkbenchSlot.Side ? displaced : normalized.SidePane,
            BottomPane = destination == WorkbenchSlot.Bottom
                ? pane
                : origin == WorkbenchSlot.Bottom ? displaced : normalized.BottomPane
        };
    }

    public WorkbenchPanePlacement PlacementFor(WorkbenchPane pane) => PlacementFor(SlotOf(pane));

    public static WorkbenchPanePlacement PlacementFor(WorkbenchSlot slot) => slot switch
    {
        WorkbenchSlot.Primary => new WorkbenchPanePlacement(Row: 1, Column: 1, RowSpan: 1, ColumnSpan: 1),
        WorkbenchSlot.Secondary => new WorkbenchPanePlacement(Row: 1, Column: 3, RowSpan: 1, ColumnSpan: 1),
        WorkbenchSlot.Side => new WorkbenchPanePlacement(Row: 1, Column: 5, RowSpan: 3, ColumnSpan: 1),
        WorkbenchSlot.Bottom => new WorkbenchPanePlacement(Row: 3, Column: 1, RowSpan: 1, ColumnSpan: 3),
        _ => throw new ArgumentOutOfRangeException(nameof(slot), slot, null)
    };
}
