# English / Vietnamese localization verification

Verified on Windows on 2026-10-31 with a local .NET 8 SDK.

## Behavior

- The language selector is at the top of **Hotkeys & Reference / Phím tắt & Hướng dẫn**.
- English and Vietnamese apply immediately to navigation, buttons, tooltips, empty states, instructions, existing status banners and app status rows.
- `Language` is saved as `en` or `vi` alongside existing configuration. Missing, null, and unsupported language values fall back to English. Legacy array configurations remain readable.
- Translation affects presentation only. Original service messages remain available for error checks; application names, file paths, shortcut combinations and native error details remain intact.
- A configuration write failure leaves the session usable and displays a translated warning.

## Validation

- The source snapshot before this change built with zero warnings and zero errors.
- The updated Release build and Windows x64 self-contained single-file publish succeeded.
- `dotnet run --project tests/StreamGuard.Localization.Tests.csproj -c Release` passed **1,388 assertions**.
- Tests cover catalog completeness and placeholders, legacy literals, nested diagnostics, Unicode and special characters in app names, language normalization, corrupt/legacy configurations, save failures, persistence, repeated language switching, hotkey parsing, preservation of configuration and simulated active protection state, completion of asynchronous window scanning, search preservation and keyboard activation of the language selector in passive focus mode.
- The actual compiled WPF window and item templates are constructed with temporary configuration. Tests suppress application startup, do not show a native window, and do not activate system protection. All four views are rendered in both languages at 860×600 and 640×460. No WPF data-binding errors were reported. Layout snapshots were also visually inspected.
- The implementation files for hotkey registration, virtual desktops, threat containment, privacy curtain, physical output protection, cursor protection, and the app model are unchanged from the source snapshot.

## Limits

These checks do not exercise firewall/hosts changes, camera or microphone permissions, capture behavior in OBS/Teams, or moving/killing real managed applications. Those operations are intentionally excluded from the regression harness. External batch and PowerShell scripts retain their own text.

## Run the updated build

Both `PalStatus/release/StreamGuard.exe` and the existing `StreamGuard/bin/Release/net8.0-windows/win-x64/publish/StreamGuard.exe` receive the same new executable. An already running instance must be exited and reopened once to load the update; subsequent language switches require no restart.

Original sources and the two original executable files are backed up outside the project in `%LOCALAPPDATA%/CodexCleanupReports/StreamGuard-before-localization-20261031-011536`.

The local SDK used for validation is `%LOCALAPPDATA%/StreamGuardBuildTools/dotnet/dotnet.exe`. It is installed outside the project without changing the system PATH.
