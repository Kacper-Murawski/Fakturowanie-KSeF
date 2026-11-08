// Motyw jasny/ciemny (D50): ustawiany przed pierwszym rysowaniem strony (bez mignięcia jasnego tła).
// Wybór użytkownika w localStorage; bez wyboru - ustawienie systemu. Plik lokalny - zgodny z CSP (script-src 'self').
(function () {
    var key = 'ksef.theme';
    var colors = { light: '#047857', dark: '#0b1114' };

    function stored() {
        try { return localStorage.getItem(key); } catch (e) { return null; }
    }

    function apply(theme) {
        document.documentElement.setAttribute('data-bs-theme', theme);
        var meta = document.querySelector('meta[name=theme-color]');
        if (meta) meta.setAttribute('content', colors[theme]);
    }

    var initial = stored();
    if (initial !== 'light' && initial !== 'dark')
        initial = window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
    apply(initial);

    window.ksefTheme = {
        get: function () { return document.documentElement.getAttribute('data-bs-theme') || 'light'; },
        toggle: function () {
            var next = window.ksefTheme.get() === 'dark' ? 'light' : 'dark';
            apply(next);
            try { localStorage.setItem(key, next); } catch (e) { }
            return next;
        }
    };
})();
