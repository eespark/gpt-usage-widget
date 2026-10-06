# GPT Usage Widget

한국어 안내는 [한국어 README](README.ko.md)를 확인하세요.

배포 ZIP에서 문서를 읽는 경우 화면 이미지와 연결된 안내는 [온라인 README](https://github.com/eespark/gpt-usage-widget#readme)에서 확인하세요.

A local Windows taskbar widget for your remaining Codex 5-hour and weekly quota. Includes light and dark themes, reminders, usage history, and exhaustion estimates.

## UI previews

These images use simulated data, not a real account. Both light and dark themes are available in either language edition.

| View | Light theme | Dark theme |
| --- | --- | --- |
| Taskbar widget | <img src="assets/screenshots/en/widget-light.png" width="340" alt="English taskbar widget in light mode"> | <img src="assets/screenshots/en/widget-dark.png" width="340" alt="English taskbar widget in dark mode"> |
| Usage details | <img src="assets/screenshots/en/details-light.png" width="300" alt="English usage details panel in light mode"> | <img src="assets/screenshots/en/details-dark.png" width="300" alt="English usage details panel in dark mode"> |
| Usage history | <img src="assets/screenshots/en/history-light.png" width="340" alt="English usage history in light mode"> | <img src="assets/screenshots/en/history-dark.png" width="340" alt="English usage history in dark mode"> |

Settings:

<img src="assets/screenshots/en/settings-light.png" width="420" alt="English settings with a fixed action footer">

## Download and run

1. Install the Codex app or CLI on Windows x64 and sign in with your ChatGPT account.
2. Download and extract your language edition from [the latest release](https://github.com/eespark/gpt-usage-widget/releases/latest).
3. Run `GPTUsageWidget.en.exe` for English or `GPTUsageWidget.exe` for Korean. Run one edition at a time.

Requires .NET Framework 4.8. No separate .NET SDK or administrator access is needed. The widget uses the installed Codex CLI and its sign-in. Signing in to ChatGPT in a browser alone is insufficient.

This is an unofficial project. It displays Codex quota, rather than every ChatGPT model's separate chat limit.

## Use

- Bars and percentages show remaining quota: 5-hour above, weekly below.
- Hover for details, click the estimate card for history, and click outside to dismiss.
- Click and release to refresh; drag to move the widget.
- Right-click for settings, Windows startup, and Exit. Use **Show widget again** in the tray menu if it disappears.
- Settings include dark mode, text and bar size, visible sections, and reminder thresholds.
- Reset credits and expiration dates appear when available, with emphasis from D-7.

Times and reminders use Korea Standard Time (UTC+9). The widget displays on the primary taskbar and hides for fullscreen apps or a hidden taskbar. Move it if it overlaps taskbar buttons.

## Reminders and refresh

Choose separate low-quota thresholds and reset reminders in Settings. Defaults are 20% remaining, 60 minutes before the 5-hour reset, and 12:30 PM on the day before the weekly reset. Identical reminders do not repeat within the same cycle.

Refreshes normally every 60 seconds, or 120 seconds on battery. Automatic polling pauses while offline, locked, or asleep and resumes afterward. Repeated failures increase the retry interval up to 15 minutes. Click to refresh immediately. The widget cannot collect records or notify while it or the PC is off.

Refreshing quota does not submit a model inference request or spend reset credits.

## History and local data

Select 24 hours, 7 days, 30 days, or another available range at the bottom of History. Your last selection survives restart. The 5-hour series is blue and weekly is purple. Small rings mark a 100% start, a 0% end, or the last observation before reset.

Lines visually join polling gaps; unobserved consumption is excluded from calculations. Bars show hourly consumption for the 24-hour view, or 24 equal intervals for longer views. A drop from 70% to 60% is 10 percentage points.

Retention is 90 days by default, selectable as 30/90/180/365 days. Display range and retention are separate. Select a longer range if older records are outside the current view. Records that were never collected cannot be recovered afterward.

Settings and records are stored in `%LOCALAPPDATA%\GPTUsageTaskbar\` on each PC, without automatic synchronization. Saving history retains a recovery backup. **Delete history** removes both records and the backup. Expired records do not return when retention is increased.

The widget does not store passwords, API keys, or conversations. Quota retrieval uses your existing Codex sign-in and internet connection.

## Estimates

**Continuous work** estimates how long quota will last at the observed work intensity without breaks. **Weekly days** uses average consumption on completed observed dates and is independent of continuous-work hours.

Average work hours are an approximation from positive-consumption intervals. Estimates wait when data is insufficient and can vary with task type and missing observations. Forecast lookback is selectable as 1/3/7/14 days, default 7.

계산의 의미는 [한국어 예측 안내](분석%20기간과%20예측.md)를 확인하세요.

## Troubleshooting

- If quota is unavailable, check Codex sign-in and internet access, then click the widget.
- If CLI discovery fails, set `CODEX_USAGE_CLI` to the full path to `codex.exe`.
- If history ends earlier than expected, check the last refresh time and selected range. The widget must be running to collect records.
- Executables are unsigned. Follow your organization's policy and verify the source of the download if Windows blocks execution.

## Build from source

Run in PowerShell from the source directory. Builds also run automated checks.

```powershell
.\build.ps1
.\build.ps1 -English
```

Executables are created in `bin`. Create both release ZIPs and SHA-256 files in `dist`:

```powershell
.\package.ps1
```

## License

Released under the [MIT license](LICENSE). Preserve the copyright and license when modifying or redistributing. See [the changelog](CHANGELOG.md).

Reference projects: [CodexPeek](https://github.com/lch5518/CodexPeek), [codex-usage-monitor](https://github.com/upstream-ray/codex-usage-monitor).
