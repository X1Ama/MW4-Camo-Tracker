# Mastery Camo Tracker (Windows app)

A native Windows program (WPF, no webview). Clients sign in and see their own progress. Owners and admins pick a client and tick things for them.

## One-time server setup (about 15 minutes)

1. Create a free project at https://supabase.com.
2. Open **SQL Editor**, paste all of `supabase.sql`, press **Run**.
3. Open **Project Settings > API**. Copy the **Project URL** and the **anon public** key.
4. Create the first account: **Authentication > Users > Add user**, tick *Auto Confirm User*.
5. In the SQL Editor, make that account the owner:

   ```sql
   update public.profiles set role = 'owner' where email = 'you@example.com';
   ```

## Adding people

| Who | How |
|---|---|
| Client | Authentication > Users > Add user (Auto Confirm). Use `username@tracker.local` as the email, e.g. `hoodie@tracker.local` (never a real email); the client types just `hoodie` in the User box. They show up automatically as role `client`. Set a display name: `update public.profiles set display_name = 'Hoodie' where email = '...';` |
| Admin | Add user, then `update public.profiles set role = 'admin' where email = '...';` |
| Give an admin clients | `insert into public.admin_clients (admin_id, client_id) select a.id, c.id from public.profiles a, public.profiles c where a.email = 'admin@example.com' and c.email = 'client@example.com';` |

Owners see every client. Admins see only the clients assigned to them. Clients see only themselves and cannot change anything.

## Build the .exe

Needs the .NET 8 SDK.

```
dotnet publish -c Release -r win-x64 -o dist
```

`dist\MasteryCamoTracker.exe` is one self-contained file (about 70 MB). It runs on any Windows 10/11 PC.

## Giving it to clients

Put a `server.json` file next to the exe so clients never see server settings:

```json
{ "Url": "https://YOURPROJECT.supabase.co", "AnonKey": "YOUR_ANON_PUBLIC_KEY" }
```

The anon key is meant to be public; the row-level security rules in `supabase.sql` are what keep clients out of each other's data.

## Updating

Change `Catalog.cs` (weapons, camo names, classes, max level), rebuild, and give clients the new exe. Progress data is on the server and is unaffected.

## How it syncs

The app re-checks the server every 4 seconds and redraws if anything changed, so a tick made by an admin appears on the client's screen within a few seconds.

## Admin panel (in the app)

Run `supabase-admin.sql` once in the SQL Editor, after `supabase.sql`. Then owners and admins see an **ADMIN PANEL** button in the app.

| Who | What they can do |
|---|---|
| Owner | Create users of any role, rename, reset passwords, change roles, assign clients to admins, delete anyone but themselves |
| Admin | Create clients (assigned to them automatically), rename, reset passwords and delete **their own** clients |
| Client | No panel |

Creating users from the panel replaces the dashboard steps above. The first owner still has to be made in the dashboard with the SQL line in step 5.

## Linux and macOS

The `cross/` folder holds a second version of the app built on [Avalonia](https://avaloniaui.net), with the same
screens, sign-in, live sync, admin panel, settings and updates. It shares the server code with the Windows app
(`Api.cs`, `RealtimeClient.cs`, `Catalog.cs`). Tag a release and GitHub builds all of these:

| System | Download | Notes |
|---|---|---|
| Linux x64 / arm64 | `MW4-Camo-Tracker-linux-x64.tar.gz` | Unpack, then run `./MasteryCamoTracker`. It adds itself to the applications menu on first start. |
| macOS (Apple Silicon) | `MW4-Camo-Tracker-macOS-osx-arm64.zip` | Unzip and drag `MW4 Camo Tracker.app` to Applications. |
| macOS (Intel) | `MW4-Camo-Tracker-macOS-osx-x64.zip` | Same as above. |

The macOS app is not signed with an Apple Developer account, so the first time macOS says it cannot verify the
developer: right-click the app, choose **Open**, then **Open** again. (Or run `xattr -dr com.apple.quarantine "/Applications/MW4 Camo Tracker.app"`.)

Linux needs a normal desktop with X11 or Wayland and the usual font libraries (`libfontconfig`). The tray icon needs a
desktop that shows tray icons (KDE, XFCE, Cinnamon and GNOME with the AppIndicator extension do).

What differs from the Windows app: "Remember me" keeps the sign-in token in a file only your user account can read
(like an SSH key) instead of Windows' own encryption, the chime plays through `paplay` / `pw-play` / `aplay` on Linux
and `afplay` on macOS, and "launch when I sign in" uses an autostart entry (Linux) or a LaunchAgent (macOS).

To build one yourself: `dotnet publish cross/MasteryTracker.Cross.csproj -c Release -r linux-x64` (or `osx-arm64`,
`osx-x64`, `win-x64`). It also runs on Windows, which is handy for trying the screens: `dotnet run --project cross -- --demo`.

## Android

The same Avalonia app also builds for Android (`cross/Android`), sharing every screen with the desktop version
through `cross/Core`. On a phone it shows one column of cards, the client list in a slide-out menu, and bigger tap targets.

- **Install:** download `MW4-Camo-Tracker.apk` from the latest release on your phone and open it. Android asks once to allow
  installs from your browser or Files app.
- **Updates:** the app checks for new versions like the other versions do. Tapping UPDATE NOW downloads the new `.apk` and
  opens Android's installer, which asks you to confirm.
- **Signing:** every release is signed with the same key (GitHub secrets `ANDROID_KEYSTORE_B64` and
  `ANDROID_KEYSTORE_PASSWORD`), which is what lets a new version install over the old one. Keep a copy of that key; if it is
  lost, everyone has to uninstall and reinstall once. Without the secrets the workflow still builds, using a throw-away key.
- **Build it yourself:** needs the .NET 8 SDK with the `android` workload and a JDK 17:
  `dotnet publish cross/Android/MasteryTracker.Android.csproj -c Release -f net8.0-android -p:AndroidPackageFormat=apk`
- To preview the phone layout on a computer: `dotnet run --project cross/Desktop -- --demo --phone`.