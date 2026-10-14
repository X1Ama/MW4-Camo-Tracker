# Rolling out updates

The app checks a small `update.json` file each time it opens (and every 30 minutes while it is running). If the version in that file is newer than the app's own version:

- **Login window:** a banner appears with the release notes and an **UPDATE NOW** button.
- **Signed in:** an **UPDATE AVAILABLE** button appears at the top right.
- **Required updates:** if `"required": true`, the login window will not let anyone sign in until they update.

Clicking the button downloads the new exe, replaces the old one, and restarts the app. Nobody has to find or download anything.

## One-time setup

1. Create a GitHub repository (public, so clients can download without signing in) and push this folder to it.
2. In `server.json` (the file shipped beside the exe) add the update address:

   ```json
   {
     "Url": "https://YOURPROJECT.supabase.co",
     "AnonKey": "YOUR_ANON_PUBLIC_KEY",
     "UpdateUrl": "https://github.com/YOURNAME/YOURREPO/releases/latest/download/update.json"
   }
   ```

3. Give clients that build once (any version that includes the updater). Every later version reaches them by itself.

## Publishing a new version

Pick one:

**A. Automatic (recommended).** After the code change is pushed:

```
git tag v0.2.0
git push origin v0.2.0
```

GitHub builds the exe and creates the release, including `update.json`.

**B. From your PC.**

```
.\release.ps1 -Version 0.2.0 -Notes "What changed" -Repo YOURNAME/YOURREPO
```

then upload `release\MasteryCamoTracker.exe` and `release\update.json` to a new GitHub Release (or run the `gh release create` command it prints). Add `-Required` to force everyone to update.

## Good to know

- **Only the app changes.** Progress, accounts and passwords live on the server, so updating never touches them.
- **Database changes** (new tables or rules) are separate: run the matching SQL in Supabase *before* releasing an app version that needs it.
- **Old versions keep working** until you mark a release required, so you can roll out gently.
- **Windows SmartScreen** may warn about a new, unsigned exe the first time it runs. An in-app update does not trigger the browser download warning.
- **Rolling back:** publish a release with a higher version number that contains the old code. Version numbers only ever go up.
