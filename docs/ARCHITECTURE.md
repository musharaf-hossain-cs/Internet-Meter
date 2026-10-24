# Architecture

## Sampling pipeline

`NetworkMonitor` enumerates eligible active `NetworkInterface` instances and reads their native Windows byte statistics. Each adapter is baselined independently so interface arrival, removal, reconnect, and counter reset cannot create a false spike. Samples use `Stopwatch.GetTimestamp()` instead of wall-clock time and pass through an exponential smoother before reaching the UI.

Every accepted sample fans out to:

1. `MainViewModel` for current, peak, session, and stopwatch values.
2. `TrafficGraph` for the bounded in-memory graph.
3. `UsageStore` for locally aggregated SQLite persistence.
4. Quota and sustained-speed alert evaluation.
5. `SharedMeterPublisher`, which writes the formatted live values and display preferences to a per-user memory-mapped block consumed by the Explorer deskband.

## Taskbar surface

On Windows 10, `InternetMeter.Deskband.dll` is a native x64 in-process COM deskband loaded by Explorer. It implements `IDeskBand2`, `IObjectWithSite`, `IInputObject`, and `IPersistStream`, owns a child window inside the taskbar, and reads live values from the memory-mapped block. The elevated installer registers it under HKLM and adds it to the machine-wide approved shell-extension list, as required by Explorer. Visibility is owned by Explorer and is toggled from **taskbar right-click → Toolbars → Internet Meter**.

There is deliberately no topmost or borderless overlay fallback. Windows 11 removed the classic taskbar Toolbars/deskband host, so the embedded meter is unavailable on the stock Windows 11 taskbar while the dashboard and tray application continue to work.

## Persistence

Usage deltas are aggregated in memory by local calendar day and flushed in batches inside a SQLite transaction. WAL mode limits write blocking. A failed flush returns the batch to the pending accumulator. Settings are written through a temporary file and atomic replacement.

## Privacy boundary

No packet capture driver is installed. The application reads aggregate adapter counters and the Windows TCP ownership table. All settings, history, sessions, and logs remain local.

