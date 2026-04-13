window.viewportHelper = {
    _listeners: new Map(),
    _nextId: 1,

    isBelow(maxWidth) {
        return window.innerWidth <= maxWidth;
    },

    register(dotNetRef, methodName, maxWidth) {
        const id = this._nextId++;
        const notify = () => {
            dotNetRef.invokeMethodAsync(methodName, window.innerWidth <= maxWidth);
        };

        const handler = () => {
            window.clearTimeout(handler._timer);
            handler._timer = window.setTimeout(notify, 80);
        };

        window.addEventListener("resize", handler, { passive: true });
        this._listeners.set(id, handler);
        notify();
        return id;
    },

    unregister(id) {
        const handler = this._listeners.get(id);
        if (!handler) {
            return;
        }

        window.removeEventListener("resize", handler);
        window.clearTimeout(handler._timer);
        this._listeners.delete(id);
    },

    scrollToId(id, block = "start") {
        const element = document.getElementById(id);
        if (!element) {
            return false;
        }

        const prefersReducedMotion = window.matchMedia?.("(prefers-reduced-motion: reduce)")?.matches;
        element.scrollIntoView({
            behavior: prefersReducedMotion ? "auto" : "smooth",
            block,
            inline: "nearest"
        });

        return true;
    },

    focusById(id, select = false) {
        const element = document.getElementById(id);
        if (!element) {
            return false;
        }

        element.focus({ preventScroll: true });
        if (select && typeof element.select === "function") {
            element.select();
        }

        return true;
    }
};
