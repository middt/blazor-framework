// Middt framework JS interop helpers.
window.middt = window.middt || {};

window.middt.setTitle = (title) => { document.title = title; };

// IANA time zone of the browser (e.g. "Europe/Istanbul"), used to show server timestamps in the user's local time.
window.middt.timeZone = () => Intl.DateTimeFormat().resolvedOptions().timeZone || null;
