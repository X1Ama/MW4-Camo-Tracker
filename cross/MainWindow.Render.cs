using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace MasteryTracker;

public partial class MainWindow
{
    List<Row> Rows()
    {
        var rows = Catalog.Weapons
            .Select(w =>
            {
                var key = Catalog.Slug(w.Name);
                var cls = w.Cls.Length > 0 ? w.Cls : data.Classes.GetValueOrDefault(key, "");
                return new Row(key, w.Name, cls, w.Cls.Length > 0, null);
            })
            .ToList();
        foreach (var c in data.Customs)
        {
            var key = "c-" + c.Id[..Math.Min(8, c.Id.Length)];
            rows.Add(new Row(key, c.Name, data.Classes.GetValueOrDefault(key, c.Cls), false, c.Id));
        }
        return rows;
    }

    static Border Panel(Control child, bool done) => new()
    {
        // A finished weapon gets an even gold outline and a faint tint, not a thick left edge
        Background = done ? Res("DoneTint") : Res("Panel"),
        BorderBrush = done ? Res("DoneLine") : Res("Line"),
        BorderThickness = new Thickness(1),
        Padding = new Thickness(14, 12, 14, 12),
        Margin = new Thickness(0, 8, 0, 0),
        CornerRadius = new CornerRadius(10),
        Child = child,
    };

    void Render() => RenderCore();

    // ---- the tab and filter buttons ---------------------------------------------

    void BuildTabs()
    {
        Tabs.Children.Clear();
        foreach (var m in Catalog.Modes)
        {
            bool on = m.Id == modeId;
            var b = new Button
            {
                Content = m.Name.ToUpperInvariant(),
                FontSize = 22,
                Padding = new Thickness(18, 7, 18, 7),
                Margin = new Thickness(0, 0, 4, 0),
                Background = on ? Res("Accent") : Brushes.Transparent,
                Foreground = on ? Res("AccentFg") : Res("Fg"),
                BorderBrush = on ? Res("Accent") : Res("Line"),
            };
            b.Classes.Add("chip");
            var id = m.Id;
            b.Click += (_, _) => { modeId = id; BuildTabs(); Render(); };
            Tabs.Children.Add(b);
        }
    }

    void BuildFilters()
    {
        FilterPanel.Children.Clear();
        foreach (var (id, text) in new[] { ("all", "ALL"), ("todo", "TO DO"), ("done", "DONE") })
        {
            bool on = filter == id;
            var b = new Button
            {
                Content = text,
                FontSize = 17,
                Padding = new Thickness(12, 5, 12, 5),
                Background = on ? Res("Accent") : Res("Panel"),
                Foreground = on ? Res("AccentFg") : Res("Muted"),
                BorderBrush = on ? Res("Accent") : Res("Line"),
            };
            b.Classes.Add("chip");
            var f = id;
            b.Click += (_, _) => { filter = f; BuildFilters(); Render(); };
            FilterPanel.Children.Add(b);
        }
    }

    // ---- the weapon list ----------------------------------------------------------

    readonly Dictionary<string, Border> cardByKey = new();
    readonly Dictionary<string, TextBlock> groupCount = new();

    List<(string Id, string Label, bool Track)> ChipsFor(Row w, Mode mode)
    {
        var chips = new List<(string Id, string Label, bool Track)>();
        if (mode.Track) chips.Add(("track-" + w.Key, "Camo Track", true));
        foreach (var c in mode.Camos) chips.Add(($"{mode.Id}-{w.Key}-{Catalog.Slug(c)}", c, false));
        return chips;
    }

    /// <summary>Redraws one weapon's card in place (a tick or a level change) instead of the whole list.</summary>
    void RefreshCard(string key)
    {
        if (target == null || me == null || !cardByKey.TryGetValue(key, out var old) || old.Parent is not Panel parent)
        {
            Render();
            return;
        }
        var mode = Catalog.Modes.First(m => m.Id == modeId);
        var w = Rows().FirstOrDefault(r => r.Key == key);
        if (w == null) { Render(); return; }
        var chips = ChipsFor(w, mode);
        int d = chips.Count(c => data.Ticks.Contains(c.Id));
        bool all = d == chips.Count;
        if ((filter == "todo" && all) || (filter == "done" && !all)) { Render(); return; }   // it leaves this list

        var card = (Border)BuildRow(w, chips, d, all);
        int i = parent.Children.IndexOf(old);
        parent.Children.RemoveAt(i);
        parent.Children.Insert(i, card);
        cardByKey[key] = card;
        UpdateCounts(mode);
    }

    /// <summary>The group counters and the overall progress bar, without rebuilding anything.</summary>
    void UpdateCounts(Mode mode)
    {
        int total = 0, done = 0;
        var perGroup = new Dictionary<string, (int D, int T)>();
        foreach (var w in Rows())
        {
            var chips = ChipsFor(w, mode);
            int d = chips.Count(c => data.Ticks.Contains(c.Id));
            total += chips.Count;
            done += d;
            var cls = Catalog.Classes.Any(c => c.Code == w.Cls) ? w.Cls : "";
            var (gd, gt) = perGroup.GetValueOrDefault(cls);
            perGroup[cls] = (gd + d, gt + chips.Count);
        }
        foreach (var (code, text) in groupCount)
            if (perGroup.TryGetValue(code, out var g)) text.Text = $"{g.D}/{g.T}";
        int pct = total == 0 ? 0 : (int)Math.Round(done * 100.0 / total);
        Progress.Value = pct;
        ProgressText.Text = $"{done} / {total}  ·  {pct}%";
    }

    void RenderCore()
    {
        if (me == null || api == null) return;
        TrackerPanel.Children.Clear();
        cardByKey.Clear();
        groupCount.Clear();
        if (target == null)
        {
            TrackerPanel.Children.Add(new TextBlock
            {
                Text = me.IsStaff ? "Pick a client on the left." : "",
                Foreground = Res("Muted"),
                Margin = new Thickness(0, 20, 0, 0),
            });
            return;
        }

        var mode = Catalog.Modes.First(m => m.Id == modeId);
        int total = 0, done = 0;
        var groups = Catalog.Classes.Select(c => c.Code).Append("").ToDictionary(c => c, _ => new List<Control>());
        var groupDone = groups.Keys.ToDictionary(k => k, _ => (0, 0));

        foreach (var w in Rows())
        {
            var chips = ChipsFor(w, mode);

            int d = chips.Count(c => data.Ticks.Contains(c.Id));
            total += chips.Count;
            done += d;
            var cls = groups.ContainsKey(w.Cls) ? w.Cls : "";
            var (gd, gt) = groupDone[cls];
            groupDone[cls] = (gd + d, gt + chips.Count);

            bool all = d == chips.Count;
            if (query.Length > 0 && !w.Name.Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
            if (filter == "todo" && all) continue;
            if (filter == "done" && !all) continue;

            var built = (Border)BuildRow(w, chips, d, all);
            cardByKey[w.Key] = built;
            groups[cls].Add(built);
        }

        foreach (var (code, rows) in groups)
        {
            if (rows.Count == 0) continue;
            var (gd, gt) = groupDone[code];
            var head = new DockPanel { Margin = new Thickness(0, 22, 0, 0) };
            var countText = new TextBlock
            {
                Text = $"{gd}/{gt}",
                FontFamily = Mono,
                Foreground = Res("Muted"),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Bottom,
            };
            groupCount[code] = countText;
            head.Children.Add(countText);
            DockPanel.SetDock(countText, Dock.Right);
            head.Children.Add(new TextBlock
            {
                Text = Catalog.ClassName(code).ToUpperInvariant(),
                FontFamily = DisplayFont,
                FontSize = 22,
                Foreground = Res("Muted"),
            });
            TrackerPanel.Children.Add(head);
            var cards = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
            foreach (var r in rows) cards.Children.Add(r);
            TrackerPanel.Children.Add(cards);
        }

        if (TrackerPanel.Children.Count == 0)
            TrackerPanel.Children.Add(new TextBlock { Text = "No matches.", Foreground = Res("Muted"), Margin = new Thickness(0, 16, 0, 0) });

        TrackerPanel.Children.Add(new TextBlock
        {
            Text = "By X1Ama",
            Foreground = Res("Muted"),
            FontFamily = Mono,
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 28, 0, 4),
        });

        int pct = total == 0 ? 0 : (int)Math.Round(done * 100.0 / total);
        Progress.Value = pct;
        ProgressText.Text = $"{done} / {total}  ·  {pct}%";
    }

    static string Initials(string label) =>
        string.Concat(label.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(p => char.ToUpperInvariant(p[0])));

    /// <summary>One weapon as a card: its name and level on top, a strip of camo tiles underneath.</summary>
    Control BuildRow(Row w, List<(string Id, string Label, bool Track)> chips, int d, bool all)
    {
        var body = new StackPanel();

        // name (with the remove button for hand-added weapons)
        var title = new Grid();
        title.Children.Add(new TextBlock
        {
            Text = w.Name.ToUpperInvariant(),
            FontFamily = DisplayFont,
            FontSize = 26,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(22, 0, 22, 0),
        });
        if (CanEdit && w.CustomId != null)
        {
            var rm = new Button
            {
                Content = "×",
                Padding = new Thickness(7, 0, 7, 2),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
            };
            rm.Classes.Add("chip");
            ToolTip.SetTip(rm, "Remove weapon");
            var id = w.CustomId;
            var snapshot = data.Customs.First(c => c.Id == id);
            rm.Click += (_, _) => Mutate(
                () => data.Customs.RemoveAll(c => c.Id == id),
                () => data.Customs.Add(snapshot),
                () => api.RemoveCustomAsync(id));
            title.Children.Add(rm);
        }
        body.Children.Add(title);
        body.Children.Add(new TextBlock
        {
            Text = $"{d}/{chips.Count} camos",
            FontFamily = Mono,
            FontSize = 11,
            Foreground = Res("Muted"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 2),
        });

        if (CanEdit && !w.Fixed)
        {
            // a plain button until it is used: building a drop-down for every weapon made the list slow to draw
            var host = new ContentControl { Margin = new Thickness(0, 6, 0, 0) };
            var pick = new Button
            {
                Content = w.Cls.Length > 0 ? Catalog.ClassName(w.Cls) : "Set type…",
                FontSize = 15,
                Padding = new Thickness(10, 5, 10, 5),
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            pick.Classes.Add("chip");
            pick.Click += (_, _) =>
            {
                var combo = new ComboBox { FontSize = 12, HorizontalAlignment = HorizontalAlignment.Stretch };
                combo.Items.Add(new ComboBoxItem { Content = "Set type…", Tag = "" });
                foreach (var c in Catalog.Classes) combo.Items.Add(new ComboBoxItem { Content = c.Name, Tag = c.Code });
                combo.SelectedIndex = Math.Max(0, Array.FindIndex(Catalog.Classes, c => c.Code == w.Cls) + 1);
                combo.SelectionChanged += (_, _) =>
                {
                    if (combo.SelectedItem is not ComboBoxItem it) return;
                    var code = (string)it.Tag!;
                    if (code == w.Cls) return;
                    var old = data.Classes.GetValueOrDefault(w.Key, "");
                    Mutate(
                        () => { if (code.Length > 0) data.Classes[w.Key] = code; else data.Classes.Remove(w.Key); },
                        () => { if (old.Length > 0) data.Classes[w.Key] = old; else data.Classes.Remove(w.Key); },
                        () => api.SetClassAsync(target!.Id, w.Key, code));
                };
                host.Content = combo;
                Dispatcher.UIThread.Post(() => combo.IsDropDownOpen = true);
            };
            host.Content = pick;
            body.Children.Add(host);
        }

        body.Children.Add(BuildLevel(w));

        // the camo tiles: initials on a square, yellow when ticked, full name on hover
        var strip = new UniformGrid { Rows = 1, Margin = new Thickness(0, 12, 0, 0) };
        foreach (var c in chips)
        {
            bool on = data.Ticks.Contains(c.Id);
            var b = new Button
            {
                Content = new TextBlock
                {
                    Text = on ? "✓" : Initials(c.Label),
                    FontFamily = Mono,
                    FontSize = on ? 15 : 12,
                    FontWeight = FontWeight.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                },
                Height = 38,
                Padding = new Thickness(0),
                Margin = new Thickness(0, 0, 4, 0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Background = on ? Res("Accent") : Res("Panel2"),
                Foreground = on ? Res("AccentFg") : Res("Fg"),
                BorderBrush = on ? Res("Accent") : Res("Line"),
                IsHitTestVisible = CanEdit,
            };
            b.Classes.Add("chip");
            ToolTip.SetTip(b, c.Label);
            var cid = c.Id;
            b.Click += (_, _) =>
            {
                bool now = !data.Ticks.Contains(cid);
                Mutate(
                    () => { if (now) data.Ticks.Add(cid); else data.Ticks.Remove(cid); },
                    () => { if (now) data.Ticks.Remove(cid); else data.Ticks.Add(cid); },
                    () => api.SetTickAsync(target!.Id, cid, now),
                    w.Key);
            };
            strip.Children.Add(b);
        }
        body.Children.Add(strip);

        var card = Panel(body, all);
        card.Width = 270;
        card.Margin = new Thickness(0, 10, 10, 0);
        return card;
    }

    static readonly Geometry MinusGeo = Geometry.Parse("M0,5 L10,5");
    static readonly Geometry PlusGeo = Geometry.Parse("M0,5 L10,5 M5,0 L5,10");

    Control BuildLevel(Row w)
    {
        int lv = Math.Min(Catalog.MaxLevel, data.Levels.GetValueOrDefault(w.Key, 0));
        var g = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        g.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        g.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
        g.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        var label = new TextBlock
        {
            Text = $"LV {lv}",
            FontFamily = Mono,
            FontSize = 11,
            Width = 46,
            VerticalAlignment = VerticalAlignment.Center,
        };
        g.Children.Add(label);

        var track = new Grid { Height = 10, Background = Res("Panel2"), Cursor = CanEdit ? new Cursor(StandardCursorType.Hand) : Cursor.Default };
        var fill = new Border
        {
            Background = Res("Accent"),
            CornerRadius = new CornerRadius(3),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        track.Children.Add(fill);
        track.SizeChanged += (_, _) => fill.Width = track.Bounds.Width * lv / Catalog.MaxLevel;
        var trackBox = new Border
        {
            BorderBrush = Res("Line"),
            BorderThickness = new Thickness(1),
            Child = track,
            VerticalAlignment = VerticalAlignment.Center,
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(1),
            ClipToBounds = true,
        };
        Grid.SetColumn(trackBox, 1);
        g.Children.Add(trackBox);

        if (CanEdit)
        {
            void SetLevel(int v)
            {
                v = Math.Clamp(v, 0, Catalog.MaxLevel);
                int old = data.Levels.GetValueOrDefault(w.Key, 0);
                if (v == old) return;
                Mutate(
                    () => data.Levels[w.Key] = v,
                    () => data.Levels[w.Key] = old,
                    () => api.SetLevelAsync(target!.Id, w.Key, v),
                    w.Key);
            }
            // a box to type the level directly (digits only, 0 to the maximum)
            g.Children.Remove(label);
            var levelBox = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Width = 76 };
            levelBox.Children.Add(new TextBlock
            {
                Text = "LV",
                FontFamily = Mono,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0),
            });
            var input = new TextBox
            {
                Text = lv.ToString(),
                Width = 40,
                MinWidth = 0,
                Height = 24,
                MinHeight = 24,
                MaxLength = 3,
                FontFamily = Mono,
                FontSize = 12,
                Padding = new Thickness(2, 0, 2, 0),
                VerticalContentAlignment = VerticalAlignment.Center,
                HorizontalContentAlignment = HorizontalAlignment.Center,
            };
            ToolTip.SetTip(input, $"Type a level from 0 to {Catalog.MaxLevel}");
            input.AddHandler(InputElement.TextInputEvent, (object? _, TextInputEventArgs e) =>
                e.Handled = e.Text != null && !e.Text.All(char.IsAsciiDigit), Avalonia.Interactivity.RoutingStrategies.Tunnel);
            input.GotFocus += (_, _) => input.SelectAll();
            void CommitLevel()
            {
                if (int.TryParse(input.Text, out var typed)) SetLevel(typed);
                input.Text = Math.Min(Catalog.MaxLevel, data.Levels.GetValueOrDefault(w.Key, 0)).ToString();
            }
            input.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter) { CommitLevel(); TopLevel.GetTopLevel(input)?.FocusManager?.ClearFocus(); e.Handled = true; }
                else if (e.Key == Key.Escape) { input.Text = lv.ToString(); TopLevel.GetTopLevel(input)?.FocusManager?.ClearFocus(); e.Handled = true; }
            };
            input.LostFocus += (_, _) => CommitLevel();
            levelBox.Children.Add(input);
            Grid.SetColumn(levelBox, 0);
            g.Children.Add(levelBox);

            // press and drag along the bar to scrub the level; it is saved once, when the mouse is let go
            bool scrubbing = false;
            int LevelAt(PointerEventArgs e) =>
                Math.Clamp((int)Math.Round(e.GetPosition(track).X / Math.Max(1, track.Bounds.Width) * Catalog.MaxLevel), 0, Catalog.MaxLevel);
            void Preview(int v)
            {
                fill.Width = track.Bounds.Width * v / Catalog.MaxLevel;
                input.Text = v.ToString();
            }
            track.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(track).Properties.IsLeftButtonPressed) return;
                scrubbing = true;
                e.Pointer.Capture(track);
                Preview(LevelAt(e));
                e.Handled = true;
            };
            track.PointerMoved += (_, e) =>
            {
                if (scrubbing) Preview(LevelAt(e));
            };
            track.PointerReleased += (_, e) =>
            {
                if (!scrubbing) return;
                scrubbing = false;
                e.Pointer.Capture(null);
                SetLevel(LevelAt(e));
            };
            track.PointerCaptureLost += (_, _) =>
            {
                if (!scrubbing) return;
                scrubbing = false;
                Preview(lv);          // capture was taken away: put the bar back
            };

            var btns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 0, 0, 0) };
            foreach (var (geo, delta) in new[] { (MinusGeo, -1), (PlusGeo, 1) })
            {
                var b = new Button
                {
                    Width = 26,
                    Height = 26,
                    Padding = new Thickness(0),
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(2, 0, 0, 0),
                    Content = new Avalonia.Controls.Shapes.Path
                    {
                        Data = geo, Stroke = Res("Fg"), StrokeThickness = 1.6, Width = 10, Height = 10,
                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    },
                };
                b.Classes.Add("chip");
                var dlt = delta;
                b.Click += (_, _) => SetLevel(data.Levels.GetValueOrDefault(w.Key, 0) + dlt);
                btns.Children.Add(b);
            }
            Grid.SetColumn(btns, 2);
            g.Children.Add(btns);
        }
        return g;
    }
}
