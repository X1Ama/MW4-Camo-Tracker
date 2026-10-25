using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace MasteryTracker;

/// <summary>
/// Lets the names in a list be dragged into the order you want. While you drag, the other names slide out of the way and
/// the one you carry glides to its new place; the order is remembered on this PC.
/// </summary>
public sealed class DragOrder
{
    readonly ListBox list;
    readonly string file;
    readonly Func<IEnumerable<Profile>> all;
    readonly Action refresh;
    readonly List<string> order;
    Point start;
    Profile? pressed;

    // what the list looked like when the drag began
    List<ListBoxItem> rows = new();
    List<Profile> people = new();
    double[] tops = Array.Empty<double>();
    double[] heights = Array.Empty<double>();
    int from = -1, to = -1;

    public DragOrder(ListBox list, string file, Func<IEnumerable<Profile>> all, Action refresh)
    {
        this.list = list; this.file = file; this.all = all; this.refresh = refresh;
        order = PeopleOrder.Load(file);
        list.AllowDrop = true;
        list.PreviewMouseLeftButtonDown += (_, e) =>
        {
            start = e.GetPosition(null);
            pressed = Up<ListBoxItem>(e.OriginalSource as DependencyObject)?.DataContext as Profile;
        };
        list.PreviewMouseMove += OnMove;
        list.DragOver += OnOver;
        list.Drop += OnDrop;
    }

    /// <summary>The names in the dragged order; anyone not placed yet stays after, in their normal order.</summary>
    public List<Profile> Sort(IEnumerable<Profile> items) => items
        .Select((p, i) => (p, i))
        .OrderBy(x => { int k = order.IndexOf(x.p.Id); return k >= 0 ? k : int.MaxValue; })
        .ThenBy(x => x.i)
        .Select(x => x.p)
        .ToList();

    static T? Up<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d != null && d is not T) d = VisualTreeHelper.GetParent(d);
        return d as T;
    }

    void OnMove(object sender, MouseEventArgs e)
    {
        if (pressed == null || e.LeftButton != MouseButtonState.Pressed) return;
        var d = e.GetPosition(null) - start;
        if (Math.Abs(d.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(d.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var who = pressed;
        pressed = null;
        if (!Capture(who)) return;
        try { DragDrop.DoDragDrop(list, new DataObject("person", who), DragDropEffects.Move); }
        finally { SettleBack(); }
    }

    /// <summary>Remembers where every row sits, and fades and lifts the one being carried.</summary>
    bool Capture(Profile who)
    {
        people = list.Items.Cast<Profile>().ToList();
        rows = people.Select(p => (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(p)).ToList();
        if (rows.Any(r => r == null)) return false;
        from = people.FindIndex(p => p.Id == who.Id);
        if (from < 0) return false;
        to = from;
        tops = rows.Select(r => r.TranslatePoint(new Point(0, 0), list).Y).ToArray();
        heights = rows.Select(r => r.ActualHeight).ToArray();
        rows[from].Opacity = 0.55;
        Panel.SetZIndex(rows[from], 10);
        return true;
    }

    static void Slide(ListBoxItem row, double y)
    {
        if (row.RenderTransform is not TranslateTransform t) row.RenderTransform = t = new TranslateTransform();
        t.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(y, TimeSpan.FromMilliseconds(170))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        });
    }

    /// <summary>Puts every row back where it was (a drag that was cancelled), gliding.</summary>
    void SettleBack()
    {
        if (rows.Count == 0) return;
        foreach (var r in rows) { Slide(r, 0); r.Opacity = 1; Panel.SetZIndex(r, 0); }
        rows = new();
        from = to = -1;
    }

    void OnOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent("person") && from >= 0 ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
        if (from < 0) return;
        // where the carried name would go: before the first other row whose middle is below the mouse
        double y = e.GetPosition(list).Y;
        int j = 0;
        for (int k = 0; k < rows.Count; k++)
        {
            if (k == from) continue;
            if (y > tops[k] + heights[k] / 2) j++; else break;
        }
        if (j == to) return;
        to = j;
        // the new order of the rows, and how far each one has to move from where it started
        var idx = Enumerable.Range(0, rows.Count).Where(k => k != from).ToList();
        idx.Insert(to, from);
        double top = tops[0];
        foreach (int k in idx)
        {
            Slide(rows[k], top - tops[k]);
            top += heights[k];
        }
    }

    void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData("person") is not Profile moved || from < 0) { SettleBack(); return; }
        int target = to;
        var others = people.Where(p => p.Id != moved.Id).ToList();
        // the whole list in its current order, the moved name put next to the one it landed beside
        var ids = Sort(all()).Select(p => p.Id).ToList();
        ids.Remove(moved.Id);
        int at;
        if (others.Count == 0) at = ids.Count;
        else if (target < others.Count) at = ids.IndexOf(others[target].Id);
        else at = ids.IndexOf(others[^1].Id) + 1;
        ids.Insert(Math.Clamp(at, 0, ids.Count), moved.Id);
        order.Clear();
        order.AddRange(ids);
        PeopleOrder.Save(ids, file);
        // the rows are already where the new order puts them: clear the slide offsets and rebuild without a jump
        foreach (var r in rows)
        {
            if (r.RenderTransform is TranslateTransform t) { t.BeginAnimation(TranslateTransform.YProperty, null); t.Y = 0; }
            r.Opacity = 1; Panel.SetZIndex(r, 0);
        }
        rows = new();
        from = to = -1;
        refresh();
        e.Handled = true;
    }
}