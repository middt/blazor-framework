﻿// By Alican
function LogThis(message) {
    console.log(message);
}


function LocalStoreSet(key,value) {
    localStorage.setItem(key, value);
}


function LocalStoreGet(key) {
    localStorage.getItem(key)
}


window.JSInteropExt = {};
window.JSInteropExt.saveAsFile = (filename, type, bytesBase64) => {

        if (navigator.msSaveBlob) {
            //Download document in Edge browser
            var data = window.atob(bytesBase64);
            var bytes = new Uint8Array(data.length);
            for (var i = 0; i < data.length; i++) {
                bytes[i] = data.charCodeAt(i);
            }
            var blob = new Blob([bytes.buffer], { type: type });
            navigator.msSaveBlob(blob, filename);
        }
        else {
            var link = document.createElement('a');
            link.download = filename;
            link.href = "data:" + type + ";base64," + bytesBase64;
            document.body.appendChild(link); // Needed for Firefox
            link.click();
            document.body.removeChild(link);
        }
    }