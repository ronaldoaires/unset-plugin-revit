# UnSet BIM — Revit plugin

Revit add-in of [UnSet BIM](https://unset.com.br). First module: the families library
(browse, favorite, download and insert families). The structure is ready for more modules
(Architectural, Structural, ...).

- Revit 2025, 2026 (.NET 8) and 2027 (.NET 10)
- Server contract: `docs/api.md` in `ronaldoaires/unset` (API v1)
- Releases and update check: GitHub Releases of `ronaldoaires/unset-plugin-revit`

## Solution layout

| Project | What it holds | Depends on |
|---|---|---|
| `UnSetBim.Core` | API client (typed, follows `docs/api.md`), DTOs, token storage (DPAPI), update download + SHA-256 check, paths, log. No Revit, no WPF. | — |
| `UnSetBim.UI` | WPF theme (`Themes/Theme.xaml`), MVVM helpers, converters, `RemoteImage` (async cached images). No Revit. | Core |
| `UnSetBim.Framework` | Revit-aware layer shared by every module: `UnSetHost` (services), `IUnSetModule`, `UnSetCommand` (base command with update/sign-in/plan checks), `RevitDispatcher` (ExternalEvent queue), session, sign-in and account windows, update flow. | Core, UI |
| `UnSetBim.Modules.Families` | Families module: catalog window, detail panel, favorites, download history, download + load + placement. | Framework |
| `UnSetBim.Revit` | The add-in loaded by Revit (`App`), ribbon tab, `ModuleCatalog`. | Framework, modules |

```
Revit ──► UnSetBim.Revit.App ──► UnSetHost.Start ──► modules.OnStartup (ribbon)
                                   │
                                   ├─ background: GET /auth/me (restore session)
                                   └─ background: GET /plugin/latest (update check)

Ribbon button ──► UnSetCommand.Execute
                    1. required update pending?  → update window, stop
                    2. signed in?                → sign-in window
                    3. plan module (if any)?     → "not in your plan", stop
                    4. Run()
```

## Build

Open `UnSetBim.slnx` in Visual Studio 2022 17.13+ (or 2026) and pick a configuration:

| Configuration | Revit | Runtime |
|---|---|---|
| `Debug.R25` / `Release.R25` | 2025 | net8.0-windows |
| `Debug.R26` / `Release.R26` | 2026 | net8.0-windows |
| `Debug.R27` / `Release.R27` | 2027 | net10.0-windows |

The Revit API comes from the installed Revit (`C:\Program Files\Autodesk\Revit <year>\`) when it exists,
otherwise from the `Nice3point.Revit.Api.*` NuGet packages of that year (CI, or a machine without that Revit).
Revit installed elsewhere: `dotnet build -c Release.R26 -p:RevitInstallDir="D:\Autodesk\Revit 2026\"`.
Force the packages: `-p:UseRevitApiPackages=true`. Building `R27` needs the .NET 10 SDK.

Output: `artifacts\<Configuration>\` holds everything the installer must copy for that year.

### Debugging

A **Debug** build writes `%AppData%\Autodesk\Revit\Addins\<year>\UnSetBIM.addin` pointing to the build
output, so starting Revit loads the fresh build. It is the same file the installer writes, so the last
one (debug build or installer) wins. Run the installer again to go back to the released version.

To use a local server, create `%LocalAppData%\UnSetBIM\settings.json`:

```json
{ "api_base_url": "http://127.0.0.1:8000/api/v1/", "site_url": "http://127.0.0.1:8000/" }
```

Delete the file to go back to production. The account window shows the API in use when it is not production.

## Data on the user's machine

Everything lives in `%LocalAppData%\UnSetBIM` (per Windows user, shared by every Revit year):

| Path | Content |
|---|---|
| `session.dat` | API token + expiry, encrypted with DPAPI (current user) |
| `state.json` | last notified update version, last catalog sort |
| `Families\<slug>\<Family name>.rfa` | last downloaded file of each family |
| `Cache\Images` | thumbnails (pruned after 14 days) |
| `<year>\<version>\` | installed add-in binaries (written by the installer) |
| `Logs\unsetbim-YYYYMMDD.log` | log, 14 days kept. Never contains tokens or passwords. |
| `settings.json` | optional developer override (see above) |

## How the plugin follows the API contract

- Every request sends `Accept: application/json` and `User-Agent: UnSetBIM-Revit/<version> (Revit <year>)`.
- The token is only sent to the API host. 401 or 403 `account_inactive` on an authenticated call clears the token and closes the plugin windows (`SessionManager`).
- Sign-in: browser (device authorization, recommended, works for Google-only accounts) or e-mail + password. `device_name` = `<machine name> (Revit)`, the same for every Revit year, so one machine uses one of the 5 device slots.
- JSON bodies are sent with `Content-Length` (never chunked): the production host's firewall answers 406 to chunked POSTs.
- No account creation, password reset, purchase or subscription management in the plugin: those buttons open the site.
- The catalog always sends `revit_version` = running Revit year. Categories and brands are loaded once per window.
- "Inserir no projeto" is enabled only when `download.allowed` is true. The download endpoint is always called (no local cache shortcut), so the server applies the rule, counts the quota and records the history. A 403 between detail and download refreshes the decision.
- The plugin checks for an open document before downloading, so a download is never spent when the family cannot be loaded.
- Downloads are written to `<file>.part` and moved into place only when complete.
- Plan `modules` gate module tools only (`UnSetCommand.RequiredPlanModule`), never families.
- Updates: `GET /plugin/latest?current=` at start, at most once a day (every start while an update is pending), plus the "Atualizações" button. `update_required` blocks every command until updated; `update_available` is offered once per version. The installer is downloaded to `%TEMP%`, checked against `sha256` and run silently (`/VERYSILENT`); it works with Revit open and the new version loads on the next start.

## Adding a module

1. New project `UnSetBim.Modules.<Name>` (copy the Families `.csproj`), reference `UnSetBim.Framework`.
2. A class implementing `IUnSetModule`: `Id`, `DisplayName`, `RequiredPlanModule` (`PlanModules.Architectural`, `PlanModules.Structural` or `null`) and `OnStartup` creating its ribbon panel with `ModuleContext.CreateButton<TCommand>()`.
3. Commands derive from `UnSetCommand` and override `RequiredPlanModule` + `FeatureName` when they need a plan module. Work on the model inside `Run` (API context) or, from a window, through `UnSetHost.Current.Revit.InvokeAsync(app => ...)`.
4. Reference the project from `UnSetBim.Revit.csproj` and add one line to `ModuleCatalog`.

## Installer

Inno Setup 6, script `installer/UnSetBIM.iss`. Per-user, no administrator rights:

- binaries in `%LocalAppData%\UnSetBIM\<year>\<version>\`, one folder per version (no locked DLL is overwritten, so updates run with Revit open);
- manifest `%AppData%\Autodesk\Revit\Addins\<year>\UnSetBIM.addin`, built from `UnSetBim.Revit/UnSetBim.addin`;
- installs only for the Revit years found on the machine (registry or `Revit.exe`);
- older version folders are removed by the next install;
- uninstall removes the manifests and `%LocalAppData%\UnSetBIM` (binaries, cache, log and the stored token).

Local build (all years + installer): `.\installer\build.ps1` (or `-Version 1.0.1`, `-Years 2025,2026`).
Output: `installer\output\UnSetBIM-Setup-<version>.exe`.

## Releasing

1. Write the release notes in Portuguese: `docs/releases/vX.Y.Z.md` (shown to users in the update window).
2. Commit, then tag and push: `git tag vX.Y.Z` and `git push origin vX.Y.Z`. Tags are numeric only (`v1.4.0`).
3. GitHub Actions (`.github/workflows/release.yml`) builds 2025/2026/2027, the installer, and publishes the release with one `.exe` asset. The API announces it within 15 minutes.
4. Betas: mark the release as *pre-release* on GitHub (the API ignores pre-releases).
5. Breaking API change: raise `PLUGIN_MIN_VERSION` in the server `.env`; older add-ins get `update_required`.

## Known points

- Thumbnails are WebP. WPF decodes them through the Windows WebP codec (present on Windows 11 and most Windows 10 machines). Without it, the card shows a placeholder instead of the image.
- The installer and the add-in DLLs are not code-signed yet: Windows SmartScreen and Revit ("Autor desconhecido") warn on first use.
