# Pingy

A portable Windows app for monitoring multiple IP addresses with ICMP ping. Published executables include .NET and run as the current user, without an installer or an administrator prompt.

## Use

1. Open `pingy.exe`, choose **Manage hosts**, then **Add host**.
2. Enter a name and literal IPv4/IPv6 address. Group and notes are optional. Press **Save**.
3. Select hosts or entire groups in the sidebar, set **Interval (s)** and **Timeout (s)**, then press **Start**.
4. **Stop** keeps the completed session on screen. Starting again resets the log and statistics.

The default list is empty. Updates from v1.0 remove its three unchanged factory entries. Entries whose name, address, group or notes were edited are preserved, together with other hosts and settings.

## Interface and results

The English interface uses white surfaces, coral `#E7717D`, gray `#C2CAD0`, sand `#C2B9B0`, brown `#7E685A`, and green `#AFD275`. Darker green/red response text maintains readability on white. The supplied penguin artwork is embedded in the executable and used for the window/taskbar icon.

Tables have flat header backgrounds, alternating rows and subtle horizontal dividers. Headers remain visible; the time column is frozen horizontally. **Auto-scroll** follows new rows. Vertical scrolling pauses it; check it again to resume.

Statistics show latest, average, minimum and maximum latency, loss, sent requests and replies. Missing replies count as loss and are excluded from latency averages. Submillisecond responses display as `<1 ms`. The log keeps up to 10,000 rows / 500,000 ping values; statistics cover the whole session. The log is not saved between launches. **Ctrl+C** copies selected cells with headers.

**Local network** lists active adapters, IP addresses and gateways. It refreshes every 15 seconds or with **Refresh**. Hover over an address cell for its full contents. This panel describes adapters, not per-destination routing.

Drag the separators between **Hosts**, **Ping log**, **Session statistics**, and **Local network** to resize them. Each panel can be closed from its header and restored from **View**. The arrow button opens a live panel in its own resizable window; closing that window returns it to the dashboard. **Local network** uses its available height and scrolls only when its rows no longer fit.

App-owned dialogs are English. Windows-owned file pickers and system menus follow the Windows display language. User-entered host/group names and adapter names are not translated.

## Hosts and configuration

Double-click or press **F2** to edit a cell. Ctrl/Shift selects multiple rows for removal. A blank group becomes **Ungrouped**. **Save** applies editor changes; **Cancel** discards them. Host selection and timing changes require stopping first.

**Export** writes a validated JSON profile. **Import** checks the file and previews counts before offering:

- **Replace all**: import hosts and settings, retaining the current local profile identity.
- **Add new**: preserve existing hosts/settings and add new IPs; duplicates keep existing names, groups and selection.
- **Cancel**: leave the profile unchanged.

Up to 256 hosts and 4 MB config files are supported. Invalid/duplicate IPs and empty names are rejected.

## Share an app with hosts inside

Configure hosts in a published portable build, stop monitoring, then choose **Export app with hosts** and save to a new filename. Transfer that EXE to a compatible PC: hosts, groups, enabled selection and timings are embedded, with no JSON import needed.

Each exported app gets a new profile identity. Local settings live in `%LOCALAPPDATA%\pingy\profiles\<id>.json`, with a previous version in `.bak`. Export the app again to share later changes. Copying an existing EXE in Explorer does not embed settings held only in the local profile.

## Compatibility

Targets Windows 10/11 versions in the [.NET 10 supported OS list](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md#windows).

| Build | Architecture |
| --- | --- |
| `win-x64` | Intel/AMD 64-bit Windows; usual choice |
| `win-x86` | 32-bit Windows 10 |
| `win-arm64` | Windows ARM64 |

Windows 7/8/8.1 are unsupported. Corporate policies can restrict EXE execution or ICMP. An ICMP timeout alone does not prove a server is down. DNS names, URLs and TCP ports are not supported.

Up to 32 hosts are checked concurrently. Rounds never overlap, so a slow round can extend the interval. Stop cancels waiting immediately; Windows finishes already-issued ICMP requests in the background.

## Build and verify

Requires Windows and .NET SDK 10. Open `Pingy.slnx` or run:

```powershell
dotnet build .\src\Pingy.App\Pingy.App.csproj -c Release
dotnet run --project .\tests\Pingy.Tests -c Release
.\scripts\publish.ps1 -Runtime win-x64
```

Use `win-x86` or `win-arm64` for other architectures. Output: `artifacts\<Runtime>\pingy.exe`. The first runtime restore needs NuGet access. No third-party packages are used.

Verify published executables and repeated host embedding:

```powershell
dotnet run --project .\tests\Pingy.Tests -c Release -- --portable-smoke artifacts/win-x64/pingy.exe artifacts/smoke
```

`src/Pingy.App/defaults.json` is embedded at build time. `Assets/pingy.png` is the original artwork; `scripts/make-icon.ps1` converts it into a multi-resolution ICO without cropping or recoloring. The ICO is checked in; regeneration is optional.

GitHub Actions tests/builds x64, x86 and ARM64 and uploads executable artifacts. x64/x86 run published-executable checks. ARM64 is cross-compiled, without an ARM UI runtime test.

Table references: [Fluent UI DataGrid](https://react.fluentui.dev/?path=/docs/components-datagrid--docs) and [WinForms border styles](https://learn.microsoft.com/en-us/dotnet/desktop/winforms/controls/change-the-border-and-gridline-styles-in-the-datagrid).
