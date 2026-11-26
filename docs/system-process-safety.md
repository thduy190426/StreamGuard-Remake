# Running Manager process safety

The Running Explorer/Running Manager picker and managed-app actions share `ProcessSafetyService`.

- Case-insensitive exact names exclude core OS processes, the shell, input/search hosts, service hosts, Defender/security components and system maintenance processes. Both the configured process name and executable basename are checked.
- Core Windows directories (`SystemApps`, `servicing`, `System32`, `SysWOW64`) are excluded. The ordinary desktop utilities Notepad, Paint, Calculator and Snipping Tool are allowed through the directory rule but still require successful runtime checks.
- Before manipulating a window or managed PID, read-only Windows queries verify its executable path, interactive session and `IsProcessCritical` result. Session 0, critical, inaccessible, missing and invalid processes are rejected. No query grants termination or memory-write rights.
- Window actions compare the expected PID when a saved owner is available. An owner mismatch does not authorize acting on a reused HWND.
- The rule applies to picker refresh, direct add/double-click, foreground hotkeys, drag/drop, legacy configuration load/save, secure launch, protection/reconciliation, hide/show/focus, virtual-desktop movement and wrapper actions. Unsafe foreground hotkeys do not silently select a different application.
- Managed-app emergency termination rechecks each PID and does not terminate an unchecked descendant process tree. The legacy-instance cleanup and experimental secure-desktop cleanup use the same process safety check. Existing containment helper timeout behavior is outside the Running Manager candidate/action flow.
- If recovery cannot verify a live window's owner, it preserves that window and its recovery state and reports the failure. It does not destroy a wrapper around an unverified child.

Existing configuration files are not rewritten merely by loading them. Protected identities are omitted from the loaded app list and from subsequent saves. Language, ordinary app preferences and hotkeys remain intact. This changes application selection and manipulation; it does not delete Windows files or hide operating-system processes from Task Manager.

## Verification

**1,898 checks passed**, including policy exceptions for ordinary apps, critical/session-zero/unreadable simulated processes, native read-only checks, invalid/reused window owners, legacy configuration migration and direct UI/hotkey/launch entry points. Build completed with zero warnings/errors. Tests do not hide, terminate or reparent actual system windows.

Windows API references: [IsProcessCritical](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-isprocesscritical), [QueryFullProcessImageNameW](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-queryfullprocessimagenamew).
