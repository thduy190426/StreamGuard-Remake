# Current-page copy/paste tools

Verified on Windows on 2026-10-31: Release build completed with zero warnings and errors; **1,478 C#/WPF checks** and **64 Chrome browser checks** passed. The script tested in Chrome is generated from the compiled embedded resource, in both application languages. The bookmarklet is exercised by clicking a real `javascript:` link, not only by evaluating its decoded text.

The new Privacy & Security card offers a Console script, an encoded bookmarklet, and a disable command. The JavaScript is embedded in the executable; its status messages and the WPF card support English and Vietnamese.

The script restores native copy, cut, paste, context-menu and selection defaults by suppressing the page's cancellation of those specific actions. It also handles `beforeinput` paste/cut cancellation, CSS selection locks, inline `!important`, and new DOM content. Ordinary keyboard shortcuts, editing restrictions (`readonly` / `disabled`), other tabs and other frames keep their behavior. Re-running does not stack patches. Disable restores original event methods, window handlers and script-owned styles; reload resets the page normally.

Verification uses a local test fixture with event blockers registered before the unlock, including an earlier Window capture listener and `returnValue = false`. Chrome runs with an isolated test profile. Tests exercise real mouse selection and Ctrl+C, Ctrl+V, Ctrl+X with input, textarea, contenteditable and Vietnamese Unicode. They also exercise frame isolation, explicit frame activation, repeated activation, dynamically added content, bookmarklet decoding, disabling and reloading. No private website or user browser profile is used.

Browser defaults and limits follow the [Clipboard API and events specification](https://www.w3.org/TR/clipboard-apis/). This tool does not override browser permissions, origin restrictions, disabled/read-only fields, CSP or specialized editor models. It does not transfer data over the network.
