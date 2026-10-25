using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace MasteryTracker;

public partial class MainView
{
    // ---- chrome text ----------------------------------------------------------

    void SetStatus(string text, bool ok)
    {
        StatusText.Text = text;
        StatusText.Foreground = ok ? Res("Muted") : Res("Warn");
    }

    void UpdateTitle()
    {
        TitleText.Text = target == null ? "MW4 CAMO TRACKER" : target.Label;
        KickerText.Text = me.IsStaff ? "MANAGING" : "MW4 CAMO TRACKER";
    }

    // ---- clients (staff) ------------------------------------------------------

    DateTime lastFullLoad = DateTime.MinValue;

    /// <summary>The client list that was requested during sign-in, or a fresh request if that one failed.</summary>
    async Task<List<Profile>> TakeClientsAsync()
    {
        var t = api.PrefetchedClients;
        api.PrefetchedClients = null;
        if (t != null)
        {
            try { return await t; } catch { /* fall through and ask again */ }
        }
        return await api.GetManagedClientsAsync();
    }

    /// <summary>A client's first screen: use the data requested during sign-in when there is some.</summary>
    async Task InitialLoadAsync()
    {
        var pre = api.PrefetchedOwnData;
        api.PrefetchedOwnData = null;
        if (pre != null)
        {
            try
            {
                var d = await pre;
                MoveOldTicks(d, me.Id);
                data = d;
                lastSig = d.Signature();
                lastFullLoad = DateTime.UtcNow;
                Timing.Mark("own data received");
                Render();
                Timing.Mark("first screen drawn");
                Timing.Finish();
                SetStatus("Live · checked " + DateTime.Now.ToString("HH:mm:ss"), true);
                FadeIn();
                return;
            }
            catch { /* ask again below */ }
        }
        await RefreshAsync();
    }

    async Task LoadClientsAsync()
    {
        try
        {
            // only clients belong in this list: never the signed-in person, never another Grinder or owner
            clients = (await TakeClientsAsync()).Where(c => c.Role == "client" && c.Id != me.Id).ToList();
            Timing.Mark("client list received");
            ApplyClientFilter();
            if (clients.Count == 0)
                SetStatus("No clients assigned to you yet. Ask an owner to assign some.", false);
            else if (target == null || clients.All(c => c.Id != target.Id)) ClientList.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            SetStatus("Could not load clients: " + ex.Message, false);
        }
    }

    bool applyingFilter;

    void ApplyClientFilter()
    {
        var q = ClientSearch.Text?.Trim() ?? "";
        var keep = target?.Id;
        var list = clients
            .Where(c => q.Length == 0 || c.Label.Contains(q, StringComparison.OrdinalIgnoreCase)
                        || c.Email.Contains(q, StringComparison.OrdinalIgnoreCase))
            .ToList();
        applyingFilter = true;
        try
        {
            ClientList.ItemsSource = list;
            if (keep != null) ClientList.SelectedItem = list.FirstOrDefault(c => c.Id == keep);
        }
        finally { applyingFilter = false; }
        var renewed = clients.FirstOrDefault(c => c.Id == keep);
        if (renewed != null && target != null && me != null && target.Id != me.Id) { target = renewed; UpdateTitle(); }
    }

    /// <summary>One line of the client list: the name, and a green or red dot for whether they have the app open.</summary>
    Control ClientRow(Profile? p)
    {
        var dock = new DockPanel();
        if (p == null) return dock;
        var dot = new Ellipse
        {
            Width = 8, Height = 8, Margin = new Thickness(8, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center,
            Fill = Res(p.IsOnline ? "Good" : "Bad"),
        };
        ToolTip.SetTip(dot, p.IsOnline ? "Has the app open" : "Offline");
        DockPanel.SetDock(dot, Dock.Right);
        dock.Children.Add(dot);
        dock.Children.Add(new TextBlock { Text = p.Label, VerticalAlignment = VerticalAlignment.Center });
        return dock;
    }

    async void ClientList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (applyingFilter) return;
        if (ClientList.SelectedItem is not Profile p || p.Id == target?.Id) return;
        if (Compact) SetDrawer(false);       // the menu slides away once somebody is picked
        target = p;
        realtime?.Watch(p.Id);
        data = new ClientData();
        lastSig = "";
        needRender = true;
        UpdateTitle();
        TrackerPanel.Children.Clear();       // the real screen is drawn once, when the data has arrived
        cardByKey.Clear();
        groupCount.Clear();
        TrackerPanel.Children.Add(new TextBlock { Text = "Loading…", Foreground = Res("Muted"), Margin = new Thickness(0, 20, 0, 0) });
        await RefreshAsync();
    }

    void SearchBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        query = SearchBox.Text?.Trim() ?? "";
        searchDebounce.Stop();
        searchDebounce.Start();
    }

    // ---- sync -----------------------------------------------------------------

    bool liveInstant;
    int remoteTicket;

    /// <summary>Somebody opened or closed the app: refresh the green dots.</summary>
    void RefreshPresenceUi()
    {
        ApplyClientFilter();
        UpdateTitle();
    }

    readonly HashSet<string> pendingTables = new();
    bool pendingFull;

    /// <summary>
    /// Applies a change straight from the realtime event so it shows up without another round trip.
    /// When it cannot be applied safely the normal re-read takes over.
    /// </summary>
    bool ApplyRemoteDetail(RemoteChange c)
    {
        Dispatcher.UIThread.Post(() =>
        {
            bool applied;
            try { applied = ApplyOnUi(c); }
            catch { applied = false; }
            if (!applied) OnRemoteChange(c.Table);
        });
        return true;
    }

    bool ApplyOnUi(RemoteChange c)
    {
        var who = target;
        if (who == null || api == null) return true;                  // nothing on screen to update
        if (c.ClientId != null && c.ClientId != who.Id) return true;  // somebody else's row
        if (c.ClientId == null || inflight > 0) return false;
        bool gone = c.Type == "DELETE";
        switch (c.Table)
        {
            case "ticks" when c.CamoId != null:
                if (gone) data.Ticks.Remove(c.CamoId); else data.Ticks.Add(c.CamoId);
                break;
            case "levels" when c.WeaponKey != null:
                if (gone) data.Levels.Remove(c.WeaponKey);
                else if (c.Level != null) data.Levels[c.WeaponKey] = c.Level.Value;
                else return false;
                break;
            case "classes" when c.WeaponKey != null:
                if (gone) data.Classes.Remove(c.WeaponKey);
                else if (c.Cls != null) data.Classes[c.WeaponKey] = c.Cls;
                else return false;
                break;
            default:
                return false;
        }
        var sig = data.Signature();
        if (sig != lastSig)
        {
            lastSig = sig;
            Render();
        }
        SetStatus("Live (instant) · changed " + DateTime.Now.ToString("HH:mm:ss"), true);
        return true;
    }

    /// <summary>A change landed on the server: re-read soon (a short delay lets bursts of changes merge).</summary>
    void OnRemoteChange(string? table)
    {
        Dispatcher.UIThread.Post(async () =>
        {
            // the "just connected" ping right after a fresh load would only repeat it
            if (table == null && (DateTime.UtcNow - lastFullLoad).TotalSeconds < 4) return;
            if (table == null) pendingFull = true; else pendingTables.Add(table);
            int ticket = Interlocked.Increment(ref remoteTicket);
            await Task.Delay(40);
            if (ticket != remoteTicket) return; // a newer change will do the refresh
            for (int i = 0; i < 20 && inflight > 0; i++) await Task.Delay(100);
            string? only = !pendingFull && pendingTables.Count == 1 ? pendingTables.First() : null;
            pendingFull = false;
            pendingTables.Clear();
            await RefreshAsync(only);
        });
    }

    /// <summary>Carries ticks saved under an older camo id over to its current id (shown at once, saved in the background).</summary>
    void MoveOldTicks(ClientData d, string clientId)
    {
        var (old, neu) = Catalog.OldTickMoves(d.Ticks);
        if (old.Count == 0) return;
        foreach (var o in old) d.Ticks.Remove(o);
        foreach (var n in neu) d.Ticks.Add(n);
        if (!CanEdit) return;
        var server = api;
        _ = Task.Run(async () =>
        {
            try
            {
                await server.RestoreAsync(clientId, neu, new Dictionary<string, int>());
                await server.ResetTicksAsync(clientId, old);
            }
            catch { /* shown correctly anyway; it is saved on a later start */ }
        });
    }

    async Task RefreshAsync(string? only = null)
    {
        var who = target;
        if (who == null || api == null) return;
        try
        {
            var fresh = new ClientData();
            if (only != null) fresh.CopyFrom(data); // keep the other tables, re-read just this one
            await api.LoadAsync(who.Id, fresh, only);
            if (only == null || only == "ticks") MoveOldTicks(fresh, who.Id);
            if (inflight > 0 || who.Id != target?.Id) return;
            var sig = fresh.Signature();
            if (only == null) lastFullLoad = DateTime.UtcNow;
            if (needRender || sig != lastSig)
            {
                if (only == null) needRender = false;
                data = fresh;
                lastSig = sig;
                Timing.Mark("client data received");
                Render();
                Timing.Mark("first screen drawn");
                Timing.Finish();
            }
            SetStatus((liveInstant ? "Live (instant) · " : "Live · ") + "checked " + DateTime.Now.ToString("HH:mm:ss"), true);
            FadeIn();
        }
        catch (Exception ex)
        {
            SetStatus("Offline, retrying… " + ex.Message, false);
        }
    }

    void Redraw(string? weaponKey)
    {
        if (weaponKey != null) RefreshCard(weaponKey);
        else Render();
    }

    async void Mutate(Action apply, Action undo, Func<Task> send, string? weaponKey = null)
    {
        if (!CanEdit) return;
        apply();
        Redraw(weaponKey);
        inflight++;
        try
        {
            await send();
            lastSig = data.Signature();
        }
        catch (Exception ex)
        {
            undo();
            Redraw(weaponKey);
            SetStatus("Could not save: " + ex.Message, false);
        }
        finally
        {
            inflight--;
        }
    }
}
