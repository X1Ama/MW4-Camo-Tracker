using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;

namespace MasteryTracker;

public partial class MainView
{
    // ---- sign-in lives inside this window --------------------------------------

    void ShowLogin()
    {
        if (loginPanel == null)
        {
            loginPanel = new LoginPanel();
            loginPanel.SignedIn += (a, p, remember) => StartSession(a, p, remember);
            AddOverlay(loginPanel, 100);
        }
        loginPanel.IsVisible = true;
        loginPanel.Reset();
    }

    void HideLogin()
    {
        if (loginPanel == null) return;
        RemoveOverlay(loginPanel);
        loginPanel = null;
    }

    /// <summary>When "Remember me" was ticked last time, signs straight back in without asking for the password.</summary>
    async Task TryResumeAsync()
    {
        var token = SessionStore.Load();
        if (token == null || loginPanel == null) { SessionStore.Log(token == null ? "no saved sign-in found" : "no login screen"); return; }
        var cfg = ServerConfig.Load();
        if (!cfg.IsComplete || !cfg.RememberMe) return;
        loginPanel.SetBusy("SIGNING YOU IN…");
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                var resumed = new Api(cfg);
                Timing.Start("remembered session used");
                var profile = await resumed.ResumeAsync(token);
                SessionStore.Log("resumed ok");
                StartSession(resumed, profile, true);
                return;
            }
            catch (ApiException ex) when (ex.Status is 400 or 401 or 403)
            {
                SessionStore.Log($"server said {ex.Status}: {ex.Message}");
                var latest = SessionStore.Load();
                if (latest != null && latest != token) { token = latest; continue; }
                if (attempt == 0) { await Task.Delay(1500); continue; }
                SessionStore.Clear();
                loginPanel?.SetBusy(null);
                return;
            }
            catch (Exception ex)
            {
                SessionStore.Log("could not reach the server: " + ex.Message);
                loginPanel?.SetBusy(null);          // offline: let them try by hand and keep the saved sign-in
                return;
            }
        }
        loginPanel?.SetBusy(null);
    }

    string? popupVersion;          // the update version whose pop-up was already shown

    void StartSession(Api newApi, Profile newMe, bool remember = false)
    {
        Timing.Mark("main screen taking over");
        api = newApi;
        me = newMe;
        if (loginPanel != null)
        {
            loginPanel.SetBusy(null);
            loginPanel.IsVisible = false;
        }
        if (remember)
        {
            SessionStore.Save(api.RefreshToken);
            api.RefreshTokenChanged = SessionStore.Save;   // keep the saved sign-in current as it renews
        }
        else
        {
            SessionStore.Clear();
        }
        popupVersion = null;

        clients = new();
        target = null;
        data = new ClientData();
        lastSig = "";
        inflight = 0;
        liveInstant = false;
        pendingUpdate = null;
        UpdateButton.IsVisible = false;
        query = "";
        SearchBox.Text = "";
        filter = "all";
        modeId = Catalog.Modes[0].Id;
        BuildFilters();
        BuildTabs();

        ApplyRoleChrome();
        TrackerPanel.Children.Clear();
        UpdateTitle();
        SetStatus("Loading…", true);

        if (api.SupportsRealtime)
        {
            realtime = new RealtimeClient(api.Config.Url, api.Config.AnonKey, api.GetAccessTokenAsync);
            realtime.Changed += OnRemoteChange;
            realtime.Detail = ApplyRemoteDetail;
            realtime.PresenceChanged += () => Dispatcher.UIThread.Post(RefreshPresenceUi);
            realtime.PeopleChanged += () => Dispatcher.UIThread.Post(OnPeopleChanged);
            realtime.CameOnline += id => Dispatcher.UIThread.Post(() => OnClientOnline(id));
            realtime.TrackPresence(me.Id);
            realtime.ConnectionChanged += ok => Dispatcher.UIThread.Post(() => liveInstant = ok);
            if (!me.IsStaff) realtime.Watch(me.Id);
        }

        timer.Interval = TimeSpan.FromSeconds(2);
        timer.Start();
        updateTimer.Start();
        _ = CheckUpdateAsync();
        _ = BeginSessionAsync();
    }

    /// <summary>The parts of the window that depend on whether the person is a client or a Grinder/owner.</summary>
    void ApplyRoleChrome()
    {
        AdminButton.IsVisible = me.IsStaff;
        MenuButton.IsVisible = Compact && me.IsStaff;
        UserText.Text = $"{me.Label}  ·  {Catalog.RoleLabel(me.Role)}  ·  v{Updater.CurrentText}";
        if (me.IsStaff)
        {
            SidePanel.IsVisible = !Compact || drawerOpen;
            SideCol.Width = Compact ? new GridLength(0) : new GridLength(280);
            ClientList.ItemsSource = null;
        }
        else
        {
            SidePanel.IsVisible = false;
            SideCol.Width = new GridLength(0);
            target = me;
        }
    }

    async Task BeginSessionAsync()
    {
        if (me.IsStaff) await LoadClientsAsync();
        else await InitialLoadAsync();
    }

    void EndSession()
    {
        timer.Stop();
        updateTimer.Stop();
        searchDebounce.Stop();
        realtime?.Dispose();
        realtime = null;
        Presence.Replace(new HashSet<string>());

        foreach (var overlay in new Control?[] { adminPanel, passwordPanel, updatePanel, settingsPanel, toastHost, resetConfirm })
            RemoveOverlay(overlay);
        adminPanel = null;
        passwordPanel = null;
        updatePanel = null;
        settingsPanel = null;
        toastHost = null;
        resetConfirm = null;

        TrackerPanel.Children.Clear();
        ClientList.ItemsSource = null;
        clients = new();
        data = new ClientData();
        target = null;
        if (api != null) api.RefreshTokenChanged = null;
        api = null!;
        me = null!;
    }

    void SignOut()
    {
        SessionStore.Clear();
        EndSession();
        ShowLogin();
    }

    // ---- role changes made by an admin ---------------------------------------------

    bool fadeInPending;

    async Task FadeAsync(double to, int ms)
    {
        Root.Transitions = new Avalonia.Animation.Transitions
        {
            new Avalonia.Animation.DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(ms) },
        };
        Root.Opacity = to;
        await Task.Delay(ms);
    }

    /// <summary>Brings the window content back after a role switch, as soon as the new data is drawn.</summary>
    void FadeIn()
    {
        if (!fadeInPending) return;
        fadeInPending = false;
        _ = FadeAsync(1, 220);
    }

    /// <summary>Moves this window between the client and Grinder views without reconnecting.</summary>
    async void SwitchRole(Profile fresh)
    {
        fadeInPending = true;
        await FadeAsync(0, 110);     // a short fade instead of a hard cut
        if (me == null) return;
        me = fresh;
        RemoveOverlay(adminPanel);
        RemoveOverlay(settingsPanel);
        adminPanel = null;
        settingsPanel = null;

        clients = new();
        target = null;
        data = new ClientData();
        lastSig = "";
        inflight = 0;

        ApplyRoleChrome();
        if (!me.IsStaff) realtime?.Watch(me.Id);
        TrackerPanel.Children.Clear();
        UpdateTitle();
        SetStatus("Loading…", true);
        _ = BeginSessionAsync();
        // safety net: never leave the window faded out if the data is slow
        _ = Task.Delay(1500).ContinueWith(_ => Dispatcher.UIThread.Post(FadeIn));
    }

    int peopleTicket;

    /// <summary>
    /// An admin changed people somewhere: re-read this person's own profile (role, name) and, for staff, the
    /// client list. A changed role switches this window between the client and Grinder views on the spot.
    /// </summary>
    async void OnPeopleChanged()
    {
        int ticket = Interlocked.Increment(ref peopleTicket);
        await Task.Delay(40);                        // a burst of changes becomes one refresh
        if (ticket != peopleTicket || api == null || me == null) return;
        var current = api;
        try
        {
            var clientsTask = current.GetManagedClientsAsync();
            if (me.IsStaff) current.PrefetchedClients = clientsTask;
            var fresh = await current.GetMyProfileAsync();
            if (ticket != peopleTicket || api != current) return;
            if (fresh == null) { SignOut(); return; }

            if (fresh.Role != me.Role)
            {
                current.PrefetchedOwnData = null;
                current.PrefetchedClients = fresh.IsStaff ? clientsTask : null;
                SwitchRole(fresh);
                return;
            }

            if (fresh.Label != me.Label)
            {
                me = fresh;
                UserText.Text = $"{me.Label}  ·  {Catalog.RoleLabel(me.Role)}  ·  v{Updater.CurrentText}";
            }
            if (me.IsStaff)
            {
                await LoadClientsAsync();
                if (adminPanel != null) await adminPanel.ReloadAsync();
            }
        }
        catch (ApiException ex) when (ex.Status is 401 or 403)
        {
            SignOut();   // the account was removed
        }
        catch { /* the next change or the safety refresh will catch up */ }
    }
}
