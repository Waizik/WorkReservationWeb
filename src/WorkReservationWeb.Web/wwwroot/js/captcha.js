// Thin wrapper around the Cloudflare Turnstile widget. All functions are safe to call
// when the Turnstile script failed to load or the widget was destroyed (e.g. by a
// Blazor re-render or token expiry); they report the widget as unavailable instead
// of throwing, so the caller can render it again.
window.workReservationCaptcha = (function () {
    let widgetId = null;

    function isWidgetAlive() {
        if (widgetId === null || typeof turnstile === "undefined") {
            return false;
        }

        try {
            turnstile.getResponse(widgetId);
            return true;
        } catch {
            widgetId = null;
            return false;
        }
    }

    return {
        render: function (elementId, siteKey) {
            const element = document.getElementById(elementId);
            if (!element || typeof turnstile === "undefined") {
                return false;
            }

            if (isWidgetAlive()) {
                return true;
            }

            // Drop any leftover markup from a previous (dead) widget before re-rendering.
            element.replaceChildren();

            try {
                widgetId = turnstile.render(element, { sitekey: siteKey }) ?? null;
            } catch {
                widgetId = null;
            }

            return widgetId !== null;
        },

        getToken: function () {
            if (!isWidgetAlive()) {
                return "";
            }

            try {
                return turnstile.getResponse(widgetId) || "";
            } catch {
                return "";
            }
        },

        reset: function () {
            if (!isWidgetAlive()) {
                return;
            }

            try {
                turnstile.reset(widgetId);
            } catch {
                widgetId = null;
            }
        }
    };
})();
