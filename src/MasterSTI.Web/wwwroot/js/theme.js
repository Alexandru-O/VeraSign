/* MasterSTI / VeraSign — theme switching helpers.
   Used by Components/UI/ThemeToggle.razor via JS interop. */
window.verasign = window.verasign || {};

window.verasign.getTheme = function () {
    return document.documentElement.dataset.theme || 'light';
};

window.verasign.setTheme = function (theme) {
    var t = theme === 'dark' ? 'dark' : 'light';
    document.documentElement.dataset.theme = t;
    try { localStorage.setItem('theme', t); } catch (e) { /* private mode */ }
    return t;
};

window.verasign.toggleTheme = function () {
    var cur = document.documentElement.dataset.theme === 'dark' ? 'light' : 'dark';
    return window.verasign.setTheme(cur);
};
