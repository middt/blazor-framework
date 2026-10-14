// Middt framework JS interop helpers.
window.middt = window.middt || {};

// Streams a file from .NET (DotNetStreamReference) and triggers a browser download.
window.middt.downloadFile = async (fileName, streamRef, contentType) => {
    const buffer = await streamRef.arrayBuffer();
    const blob = new Blob([buffer], { type: contentType || "application/octet-stream" });
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement("a");
    anchor.href = url;
    anchor.download = fileName ?? "";
    document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();
    URL.revokeObjectURL(url);
};

window.middt.setTitle = (title) => { document.title = title; };

// IANA time zone of the browser (e.g. "Europe/Istanbul"), used to show server timestamps in the user's local time.
window.middt.timeZone = () => Intl.DateTimeFormat().resolvedOptions().timeZone || null;

// Forms marked with data-middt-autosubmit post themselves once the page loads (e.g. logout confirmation).
document.addEventListener("DOMContentLoaded", () => {
    document.querySelectorAll("form[data-middt-autosubmit]").forEach(form => form.submit());
});
