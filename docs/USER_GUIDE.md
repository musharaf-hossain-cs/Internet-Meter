# User guide

## Starting and stopping

Launch `InternetMeter.exe` from the Start menu or run the project from source. Internet Meter places an icon in the notification area and opens the dashboard unless it was started with `--background`.

Closing the dashboard does not stop monitoring. Reopen it from the notification-area icon and choose **Exit** there when you want to stop the application completely. Only one instance can run per Windows user.

## Dashboard

The dashboard is split into these tabs:

- **Live** shows the throughput graph, current rates, session totals, and peak rates.
- **Apps** shows per-process TCP and UDP download/upload rates. Windows asks for administrator approval when this monitor starts.
- **Usage & reports** shows locally aggregated daily totals and exports them to CSV.
- **Stopwatch** measures transfer totals and average speed for a user-controlled interval.
- **Connections** lists TCP endpoints, states, process IDs, and owning process names.
- **Settings** controls adapters, units, sampling, appearance, alerts, history retention, and startup behavior.

## Windows 10 taskbar toolbar

The embedded meter depends on Explorer's classic deskband interface and is available only on the Windows 10 taskbar.

1. Install Internet Meter or launch a registered build once.
2. Right-click an empty area of the taskbar.
3. Open **Toolbars**.
4. Select **Internet Meter**.

Repeat the same steps to hide it. If the entry is missing, restart Windows Explorer from Task Manager or sign out and back in once. The installer needs administrator approval because Explorer requires the shell extension to be placed on the machine-wide approved-extension list.

Windows 11 does not expose the classic Toolbars menu. The dashboard and notification-area features still work, but the embedded taskbar meter does not.

## Choosing adapters

By default, Internet Meter monitors eligible active network adapters. Use **Settings** to select specific adapters or include VPN, tunnel, and virtual interfaces.

Selecting several adapters adds their traffic together. Avoid selecting overlapping physical and virtual paths if doing so would count the same transfer twice. Adapter reconnects and counter resets are baselined before a new rate is reported, preventing false spikes.

## Rate display and smoothing

You can display rates in bytes per second or bits per second and choose a refresh interval between 250 milliseconds and 5 seconds. Smoothing reduces rapid visual changes but also makes the displayed rate react more slowly. The meter reports traffic observed on the computer; it is not an Internet speed test.

## Usage history and reports

Usage is aggregated by local calendar day and stored in SQLite. The **Usage & reports** tab can refresh the displayed totals and export them to CSV. History retention is configurable in **Settings**.

Normal mode stores application data here:

```text
%LOCALAPPDATA%\Internet Meter
```

Portable mode stores it beside the executable:

```text
Data\settings.json
Data\usage.db
```

Diagnostic logs are stored under `%LOCALAPPDATA%\Internet Meter\logs` in normal mode.

## Alerts

Internet Meter can notify when a configured percentage of a monthly quota is reached or when traffic remains above a selected speed for a selected duration. A quota of zero disables quota calculations. Alerts use the same adapters and traffic measurements as the live meter.

## Troubleshooting

### The Windows 10 toolbar is missing

- Run the installed application once so registration can complete.
- Confirm that **Internet Meter** is enabled under the taskbar's **Toolbars** menu.
- Restart Windows Explorer or sign out and back in.
- Reinstall if the deskband DLL was moved or removed.

### Per-app traffic does not appear

- Open the **Apps** tab and approve the UAC prompt.
- If approval was declined, restart Internet Meter and open the tab again.
- Some short-lived processes may finish before their names can be resolved; their process ID can still appear temporarily.

### Traffic is too high or counted twice

- Review the selected adapters.
- Disable **Include VPN, tunnel, and virtual adapters** unless those interfaces need to be measured.
- Remember that LAN and operating-system traffic are included when they cross a selected interface.

### Windows SmartScreen warns about the application

Locally built and unsigned packages can show an unknown-publisher warning. Production releases should Authenticode-sign both the application and installer with a trusted code-signing certificate.

### Reset local settings

Exit Internet Meter, then rename or remove `settings.json` from the application data directory. A new file with defaults is created on the next launch. Preserve `usage.db` if you want to keep usage history.
