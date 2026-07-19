// Thin wrapper around the Cloudflare Turnstile widget. All functions are safe to call
// when the Turnstile script failed to load; they simply report the widget as unavailable.
window.workReservationCaptcha = {
    widgetId: null,

    render: function (elementId, siteKey) {
        const element = document.getElementById(elementId);
        if (!element || typeof turnstile === "undefined") {
            return false;
        }

        if (this.widgetId !== null) {
            return true;
        }

        this.widgetId = turnstile.render(element, { sitekey: siteKey });
        return this.widgetId !== null;
    },

    getToken: function () {
        if (typeof turnstile === "undefined" || this.widgetId === null) {
            return "";
        }

        return turnstile.getResponse(this.widgetId) || "";
    },

    reset: function () {
        if (typeof turnstile !== "undefined" && this.widgetId !== null) {
            turnstile.reset(this.widgetId);
        }
    }
};
