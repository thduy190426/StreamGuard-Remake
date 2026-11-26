using System.IO;
using System.Text.Json;
using StreamGuard.Localization;

namespace StreamGuard.Services;

public static class BrowserScriptService
{
    private static readonly Lazy<string> CopyPasteTemplate = new(() =>
    {
        using var stream = typeof(BrowserScriptService).Assembly.GetManifestResourceStream("StreamGuard.BrowserScripts.EnableCopyPaste.js")
            ?? throw new InvalidOperationException("Missing copy/paste script resource.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });

    public static string GetCopyPasteScript()
    {
        var language = LocalizationService.Current;
        var messages = JsonSerializer.Serialize(new
        {
            enabled = language["Clipboard.ScriptEnabled"],
            disabled = language["Clipboard.ScriptDisabled"],
            alreadyEnabled = language["Clipboard.ScriptAlreadyEnabled"]
        });
        return CopyPasteTemplate.Value.Replace("__STREAMGUARD_CLIPBOARD_MESSAGES__", messages, StringComparison.Ordinal);
    }

    public static string GetCopyPasteBookmarklet() => "javascript:" + Uri.EscapeDataString("void " + GetCopyPasteScript());
    public static string GetAntiBlurBookmarklet() => "javascript:" + Uri.EscapeDataString("void " + GetAntiBlurScript());
    public static string GetDisableCopyPasteScript() => "window.__streamGuardClipboardUnlock?.disable();";
    public static string GetDisableAntiBlurScript() => "window.__streamGuardAntiBlur?.disable();";

    public static string GetAntiBlurScript() => @"(function(){
    const key = '__streamGuardAntiBlur';
    if (window[key]?.enabled) return;
    let enabled = true;
    const stop = (e) => {
        if (!enabled) return;
        e.stopImmediatePropagation();
        e.stopPropagation();
    };
    const events = [
        'blur', 'focusout', 'visibilitychange', 'webkitvisibilitychange', 'pagehide',
        'mouseleave', 'pointerleave', 'dragleave',
        'fullscreenchange', 'webkitfullscreenchange', 'mozfullscreenchange', 'MSFullscreenChange',
        'fullscreenerror', 'webkitfullscreenerror'
    ];
    events.forEach(ev => {
        window.addEventListener(ev, stop, true);
        document.addEventListener(ev, stop, true);
    });
    const stopMouseOut = (e) => {
        if (!enabled) return;
        if (!e.relatedTarget && !e.toElement) {
            e.stopImmediatePropagation();
            e.stopPropagation();
        }
    };
    window.addEventListener('mouseout', stopMouseOut, true);
    document.addEventListener('mouseout', stopMouseOut, true);

    const origHidden = Object.getOwnPropertyDescriptor(Document.prototype, 'hidden') || Object.getOwnPropertyDescriptor(document, 'hidden');
    const origVis = Object.getOwnPropertyDescriptor(Document.prototype, 'visibilityState') || Object.getOwnPropertyDescriptor(document, 'visibilityState');
    const origHasFocus = Document.prototype.hasFocus;
    const origFsElem = Object.getOwnPropertyDescriptor(Document.prototype, 'fullscreenElement') || Object.getOwnPropertyDescriptor(document, 'fullscreenElement');
    const origWebkitFsElem = Object.getOwnPropertyDescriptor(Document.prototype, 'webkitFullscreenElement') || Object.getOwnPropertyDescriptor(document, 'webkitFullscreenElement');
    const origWebkitIsFs = Object.getOwnPropertyDescriptor(Document.prototype, 'webkitIsFullScreen') || Object.getOwnPropertyDescriptor(document, 'webkitIsFullScreen');
    const origExitFs = Document.prototype.exitFullscreen || document.exitFullscreen;
    const origWebkitExitFs = Document.prototype.webkitExitFullscreen || document.webkitExitFullscreen;
    const origKeyboardLock = navigator.keyboard && navigator.keyboard.lock ? navigator.keyboard.lock.bind(navigator.keyboard) : null;

    try {
        Object.defineProperty(document, 'hidden', { get: () => enabled ? false : (origHidden?.get ? origHidden.get.call(document) : false), configurable: true });
        Object.defineProperty(document, 'visibilityState', { get: () => enabled ? 'visible' : (origVis?.get ? origVis.get.call(document) : 'visible'), configurable: true });
        Object.defineProperty(document, 'webkitVisibilityState', { get: () => enabled ? 'visible' : 'visible', configurable: true });
        document.hasFocus = () => enabled ? true : (origHasFocus ? origHasFocus.call(document) : true);

        // F11 Fullscreen mock & protection
        const getFullscreenElement = () => enabled ? (origFsElem?.get ? (origFsElem.get.call(document) || document.documentElement) : document.documentElement) : (origFsElem?.get ? origFsElem.get.call(document) : null);
        Object.defineProperty(document, 'fullscreenElement', { get: getFullscreenElement, configurable: true });
        Object.defineProperty(document, 'webkitFullscreenElement', { get: getFullscreenElement, configurable: true });
        Object.defineProperty(document, 'webkitCurrentFullScreenElement', { get: getFullscreenElement, configurable: true });
        Object.defineProperty(document, 'mozFullScreenElement', { get: getFullscreenElement, configurable: true });
        Object.defineProperty(document, 'msFullscreenElement', { get: getFullscreenElement, configurable: true });
        Object.defineProperty(document, 'fullscreen', { get: () => enabled ? true : false, configurable: true });
        Object.defineProperty(document, 'webkitIsFullScreen', { get: () => enabled ? true : (origWebkitIsFs?.get ? origWebkitIsFs.get.call(document) : false), configurable: true });
        Object.defineProperty(document, 'mozFullScreen', { get: () => enabled ? true : false, configurable: true });

        document.exitFullscreen = () => enabled ? Promise.resolve() : (origExitFs ? origExitFs.call(document) : Promise.resolve());
        document.webkitExitFullscreen = () => enabled ? Promise.resolve() : (origWebkitExitFs ? origWebkitExitFs.call(document) : Promise.resolve());
        if (Document.prototype.exitFullscreen) {
            Document.prototype.exitFullscreen = function() { return enabled ? Promise.resolve() : (origExitFs ? origExitFs.call(this) : Promise.resolve()); };
        }
        if (Document.prototype.webkitExitFullscreen) {
            Document.prototype.webkitExitFullscreen = function() { return enabled ? Promise.resolve() : (origWebkitExitFs ? origWebkitExitFs.call(this) : Promise.resolve()); };
        }
        if (origKeyboardLock && navigator.keyboard) {
            navigator.keyboard.lock = function() { return enabled ? Promise.resolve() : origKeyboardLock.apply(navigator.keyboard, arguments); };
        }
    } catch(e) {}

    window.onblur = null;
    window.onfocusout = null;
    document.onvisibilitychange = null;
    window.onmouseleave = null;
    document.onmouseleave = null;
    document.onfullscreenchange = null;
    document.onwebkitfullscreenchange = null;
    window.onfullscreenchange = null;

    window[key] = {
        get enabled() { return enabled; },
        disable() {
            enabled = false;
            events.forEach(ev => {
                window.removeEventListener(ev, stop, true);
                document.removeEventListener(ev, stop, true);
            });
            window.removeEventListener('mouseout', stopMouseOut, true);
            document.removeEventListener('mouseout', stopMouseOut, true);
            if (origHidden) { try { Object.defineProperty(document, 'hidden', origHidden); } catch(e){} }
            if (origVis) { try { Object.defineProperty(document, 'visibilityState', origVis); } catch(e){} }
            if (origHasFocus) { try { document.hasFocus = origHasFocus; } catch(e){} }
            if (origFsElem) { try { Object.defineProperty(document, 'fullscreenElement', origFsElem); } catch(e){} }
            if (origWebkitFsElem) { try { Object.defineProperty(document, 'webkitFullscreenElement', origWebkitFsElem); } catch(e){} }
            if (origWebkitIsFs) { try { Object.defineProperty(document, 'webkitIsFullScreen', origWebkitIsFs); } catch(e){} }
            if (origExitFs) { try { document.exitFullscreen = origExitFs; } catch(e){} }
            if (origWebkitExitFs) { try { document.webkitExitFullscreen = origWebkitExitFs; } catch(e){} }
            if (origKeyboardLock && navigator.keyboard) { try { navigator.keyboard.lock = origKeyboardLock; } catch(e){} }
            delete window[key];
        }
    };
    console.log('%c[Driver Booster]%c Focus & F11 Fullscreen Lock Active — Tab is permanently focused & in fullscreen!', 'background:#059669;color:white;padding:3px 8px;border-radius:4px;font-weight:bold;', 'color:#10b981;font-weight:bold;');
})();";
}
