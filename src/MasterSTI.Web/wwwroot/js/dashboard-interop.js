/* MasterSTI / VeraSign - Dashboard JS helpers.
   Consumed by Components/Pages/Dashboard.razor via IJSRuntime. */
window.veraDash = window.veraDash || {};

window.veraDash.scrollTo = function (id) {
    var el = document.getElementById(id);
    if (el) el.scrollIntoView({ behavior: 'smooth', block: 'start' });
};

window.veraDash.isVisible = function () {
    return typeof document.visibilityState === 'string'
        ? document.visibilityState === 'visible'
        : true;
};
