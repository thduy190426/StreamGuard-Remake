# Screen-capture protection and limits

Driver Booster now checks the Windows capture-affinity flag after applying it to its own window. A green badge means the OS reports `WDA_EXCLUDEFROMCAPTURE`; it does not mean that every recorder or every protected application has been blocked. Legacy monitor-only protection and failed/unverified calls receive different labels. Wrapper hosts use the same checked affinity service.

## Recognized tools

The process catalog contains 33 tool families. Matching is exact and case-insensitive; duplicate processes are grouped by family. The catalog includes OBS, Streamlabs, XSplit, PRISM, Bandicam, Camtasia Recorder, Snagit, ShareX, ScreenToGif, FastStone Capture, Snipping Tool, Steps Recorder, Loom and Fraps. It also reports Game Bar, Medal, NVIDIA/AMD overlays, Discord/Teams/Zoom/Webex, AnyDesk/TeamViewer/RustDesk/UltraViewer/RDP clients, common browsers and FFmpeg.

These are candidate process aliases, not a trustworthy recording detector. Names vary across releases, can be changed, and can be imitated. A matching process can be idle. RDP client presence does not detect an incoming RDP session. Browser presence does not reveal which tab or extension is recording. No capture tools are terminated, injected into, suspended or blocked by firewall rules by this feature.

## Optional desktop curtain

In **Privacy & Security → Screen Capture Protection**, the first option defaults to on and automatically covers all screens when a recognized recorder/screenshot tool is running **and app protection is active**. It remains idle until app protection starts. Users can disable it and their choice is retained. A separate option, off by default, includes multi-purpose tools such as GPU overlays, browsers, meetings and remote access; their mere presence can then trigger the curtain even if they are not recording.

- Polling happens approximately every three seconds. There can be exposed frames before detection; an unknown or renamed tool can be missed entirely.
- An incomplete scan keeps an enabled, active guard conservative: it requests the curtain rather than declaring the desktop safe.
- **Ctrl+Shift+Alt+C** removes and pauses the automatic curtain for the current protection session. Resume it with the button, toggle the guard option, or stop/start protection.
- The automatic curtain requires successful registration of that recovery shortcut. A shortcut conflict disables the curtain and displays an explanation instead of risking a screen cover without a registered recovery key.
- **Ctrl+Shift+Alt+H** stops protection and restores application windows. Stop or exit also removes the automatic curtain.
- A partial/failed curtain is reported as a failure. The manual curtain remains independently available.
- Configuration stores `CaptureGuardEnabled` and `CaptureGuardIncludeMultiPurpose`. Missing capture preferences (including legacy app arrays) default to enabled recorder protection and disabled multi-purpose triggers. Explicitly saved values are retained.

## Coverage by capture path

| Path | What this change provides | Remaining limit |
|---|---|---|
| Windows-supported window/screen capture APIs | Apply and read back capture affinity on owned windows | Specific API support matters; each third-party recorder has not been tested |
| Desktop/display capture | Optional black curtain on the visible desktop | Polling delay and unknown processes can leave gaps |
| Direct window capture | Affinity where honored | A foreground cover is insufficient for a source capturing an underlying window |
| Game/GPU hooks or custom capture drivers | Identify some common host processes and show the limitation | No universal prevention or guarantee |
| Browser tab, DOM or extension capture | Report browser presence when recognized | Tab capture can obtain rendered page content independently of a desktop overlay |
| HDMI capture card, remote capture outside this process list, external camera | Manual screen curtain can hide the visible desktop | No reliable detection or general hardware/optical capture prevention |

This feature does not change the existing disabled automatic HDMI-cover behavior, device permissions or website containment rules.

## Verification

Release build completed with no warnings/errors. **1,811 C#/WPF checks** passed: alias matching and false-positive controls, guard policy across settings/states, recovery-shortcut availability, enabled defaults for new/legacy configurations, retention of an explicit disabled preference, persistence, pause/resume/stop, partial-curtain reporting, capture-affinity failure/read-back/legacy cases, and renamed assembly/product/UI identity. The native API also returned `Excluded` with error 0 for an invisible, test-owned top-level HWND on this machine.

The transition tests use a fake curtain to avoid covering the user's desktop. Existing layout and multilingual tests run with temporary configuration and inactive system controls. Layout snapshots were inspected at normal and minimum sizes. No claim is made that real recordings from all listed products were tested or prevented. The native HWND check verifies the Windows flag, not pixel-level output in OBS or a GPU hook.

## Primary references

- [Microsoft: SetWindowDisplayAffinity](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowdisplayaffinity): applies to an owned top-level window, supported capture APIs, Windows 10 version 2004 compatibility and absence of a general security guarantee.
- [OBS: Window Capture](https://obsproject.com/kb/window-capture-sources): a specific window can be recorded even with another window in front.
- [OBS: Game Capture](https://obsproject.com/kb/game-capture-source): game capture has a separate capture/hook path.
- [Chrome: tabCapture](https://developer.chrome.com/docs/extensions/reference/api/tabCapture): extensions can capture a tab's rendered contents.
- [Microsoft: Snipping Tool](https://support.microsoft.com/en-us/windows/apps/use-snipping-tool-to-capture-screenshots), [NVIDIA recording overlay](https://www.nvidia.com/en-ph/geforce/news/nvidia-app-download-and-features/), [AMD Record & Stream](https://www.amd.com/en/resources/support-articles/faqs/DH3-023.html).
