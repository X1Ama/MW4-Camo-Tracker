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
