(() => {
    'use strict';
    const messages = __STREAMGUARD_CLIPBOARD_MESSAGES__;
    const key = '__streamGuardClipboardUnlock';
    if (window[key]?.enabled) {
        console.info('[Driver Booster] ' + messages.alreadyEnabled);
        return;
    }

    const proto = window.Event.prototype;
    const names = ['copy', 'cut', 'paste', 'contextmenu', 'selectstart', 'keydown', 'keyup', 'beforeinput'];
    const originals = Object.fromEntries(['preventDefault', 'stopPropagation', 'stopImmediatePropagation']
        .map(name => [name, proto[name]]));
    const returnValue = Object.getOwnPropertyDescriptor(proto, 'returnValue');
    const oldHandlers = new Map();
    const handlerWrappers = new Map();
    const changedStyles = new Map();
    const root = document.documentElement;
    const attribute = 'data-streamguard-clipboard-unlock';
    const oldAttribute = root.getAttribute(attribute);
    const style = document.createElement('style');
    let enabled = true;

    function isProtected(event) {
        if (!enabled) return false;
        if (['copy', 'cut', 'paste', 'contextmenu', 'selectstart'].includes(event.type)) return true;
        if (event.type === 'beforeinput') {
            return ['insertFromPaste', 'insertFromPasteAsQuotation', 'deleteByCut'].includes(event.inputType);
        }
        if (event.type !== 'keydown' && event.type !== 'keyup') return false;
        const keyName = (event.key || '').toLowerCase();
        return ((event.ctrlKey || event.metaKey) && ['a', 'c', 'v', 'x', 'insert'].includes(keyName))
            || (event.shiftKey && ['insert', 'delete'].includes(keyName));
    }

    const wrappers = {};
    for (const name of Object.keys(originals)) {
        wrappers[name] = function (...args) {
            if (!isProtected(this)) return originals[name].apply(this, args);
        };
        proto[name] = wrappers[name];
    }
    const returnValueSetter = function (value) {
        if (value === false && isProtected(this)) return;
        returnValue.set.call(this, value);
    };
    if (returnValue?.configurable && returnValue.set) {
        Object.defineProperty(proto, 'returnValue', { ...returnValue, set: returnValueSetter });
    }

    // Window handlers can precede our capture listener. Preserve them for disable().
    for (const name of names) {
        const property = 'on' + name;
        const original = window[property];
        oldHandlers.set(property, original);
        const replacement = typeof original === 'function' ? function (event) {
            if (!isProtected(event)) return original.apply(this, arguments);
        } : null;
        handlerWrappers.set(property, replacement);
        window[property] = replacement;
    }
    function guard(event) {
        if (isProtected(event)) originals.stopImmediatePropagation.call(event);
    }
    for (const name of names) window.addEventListener(name, guard, true);

    root.setAttribute(attribute, '1');
    style.textContent = ':root[' + attribute + '][' + attribute + '][' + attribute + '], '
        + ':root[' + attribute + '][' + attribute + '][' + attribute + '] * '
        + '{ user-select: text !important; -webkit-user-select: text !important; }';
    (document.head || root).appendChild(style);

    // Inline !important declarations outrank a stylesheet. Only change disabled selection.
    function allowSelection(element) {
        if (!(element instanceof window.HTMLElement)) return;
        const computed = window.getComputedStyle(element);
        if (computed.userSelect !== 'none' && computed.webkitUserSelect !== 'none') return;
        if (!changedStyles.has(element)) {
            changedStyles.set(element, ['user-select', '-webkit-user-select'].map(property => ({
                property, value: element.style.getPropertyValue(property),
                priority: element.style.getPropertyPriority(property)
            })));
        }
        element.style.setProperty('user-select', 'text', 'important');
        element.style.setProperty('-webkit-user-select', 'text', 'important');
    }
    function visit(element) {
        if (!(element instanceof window.HTMLElement)) return;
        allowSelection(element);
        for (const child of element.querySelectorAll('*')) allowSelection(child);
    }
    visit(root);
    const observer = new MutationObserver(records => {
        for (const record of records) {
            if (record.type === 'attributes') allowSelection(record.target);
            else for (const node of record.addedNodes) visit(node);
        }
    });
    observer.observe(root, { childList: true, subtree: true, attributes: true, attributeFilter: ['style', 'class'] });

    const state = {
        get enabled() { return enabled; },
        disable() {
            if (!enabled) return;
            enabled = false;
            observer.disconnect();
            for (const name of names) window.removeEventListener(name, guard, true);
            for (const name of Object.keys(originals)) {
                if (proto[name] === wrappers[name]) proto[name] = originals[name];
            }
            if (Object.getOwnPropertyDescriptor(proto, 'returnValue')?.set === returnValueSetter) {
                Object.defineProperty(proto, 'returnValue', returnValue);
            }
            for (const [property, handler] of oldHandlers) {
                if (window[property] === handlerWrappers.get(property)) window[property] = handler;
            }
            for (const [element, properties] of changedStyles) {
                for (const { property, value, priority } of properties) {
                    if (element.style.getPropertyValue(property) !== 'text'
                        || element.style.getPropertyPriority(property) !== 'important') continue;
                    if (value) element.style.setProperty(property, value, priority);
                    else element.style.removeProperty(property);
                }
            }
            changedStyles.clear();
            style.remove();
            if (root.getAttribute(attribute) === '1') {
                if (oldAttribute === null) root.removeAttribute(attribute);
                else root.setAttribute(attribute, oldAttribute);
            }
            if (window[key] === state) delete window[key];
            console.info('[Driver Booster] ' + messages.disabled);
        }
    };
    window[key] = state;
    console.info('[Driver Booster] ' + messages.enabled);
})();
