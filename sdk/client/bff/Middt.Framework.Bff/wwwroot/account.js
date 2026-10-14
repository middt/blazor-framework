// Middt BFF account pages.
// Forms marked with data-middt-autosubmit post themselves once the page loads (e.g. logout confirmation).
document.addEventListener("DOMContentLoaded", () => {
    document.querySelectorAll("form[data-middt-autosubmit]").forEach(form => form.submit());
});
