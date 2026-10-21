// Batch 4: decode the official CropCare QR locally in the browser.
// The API still performs authoritative registry/claim validation.
(() => {
    "use strict";
    const rawInput = document.getElementById("QrPayload");
    const fileInput = document.getElementById("qrFile");
    const startButton = document.getElementById("startCamera");
    const stopButton = document.getElementById("stopCamera");
    const pairButton = document.getElementById("pairButton");
    const form = document.getElementById("pairForm");
    const statusBox = document.getElementById("qrStatus");
    if (!rawInput || !form || !statusBox) return;

    let scanner = null;
    let cameraRunning = false;
    let processingScan = false;
    const hasZones = !!document.querySelector('#ZoneId option[value]:not([value=""])');

    function notice(message, type) {
        statusBox.className = "alert alert-" + type + " mt-3 mb-0";
        statusBox.textContent = message;
    }

    function parseCropCareQr(value) {
        if (typeof value !== "string" || value.length > 200) return null;
        const pieces = value.trim().split("|");
        if (pieces.length !== 3 || pieces[0] !== "CROPCARE1") return null;
        if (!/^[A-Za-z0-9_-]{6,128}$/.test(pieces[1])) return null;
        if (!/^[a-fA-F0-9]{32}$/.test(pieces[2])) return null;
        return { deviceId: pieces[1], qr: `CROPCARE1|${pieces[1]}|${pieces[2].toUpperCase()}` };
    }

    function acceptDecoded(value) {
        const parsed = parseCropCareQr(value);
        if (!parsed) {
            rawInput.value = "";
            pairButton.disabled = true;
            notice("Not a valid CropCare device QR code. Please scan an official CROPCARE1 QR label.", "danger");
            return false;
        }
        rawInput.value = parsed.qr;
        pairButton.disabled = !hasZones;
        // Never display, log, or put the claim code in the URL.
        notice(`Recognized CropCare QR for device ${parsed.deviceId}. Select a zone and press Pair Sensor. The API will verify its claim code.`, "success");
        return true;
    }

    async function stopCamera() {
        if (!scanner || !cameraRunning) return;
        try { await scanner.stop(); } catch (_) { /* Camera may already have stopped. */ }
        cameraRunning = false;
        if (startButton) startButton.disabled = false;
        if (stopButton) stopButton.disabled = true;
    }

    function ensureScanner() {
        if (typeof Html5Qrcode === "undefined") {
            notice("QR scanner library was not loaded. Check your internet connection and reload the page.", "warning");
            return false;
        }
        if (!scanner) {
            scanner = new Html5Qrcode("qr-reader", {
                formatsToSupport: [Html5QrcodeSupportedFormats.QR_CODE],
                verbose: false
            });
        }
        return true;
    }

    startButton?.addEventListener("click", async () => {
        if (!ensureScanner()) return;
        if (cameraRunning) return;
        startButton.disabled = true;
        notice("Requesting camera access...", "info");
        try {
            await scanner.start(
                { facingMode: "environment" },
                { fps: 8, qrbox: { width: 220, height: 220 } },
                async decoded => {
                    if (processingScan) return;
                    processingScan = true;
                    if (acceptDecoded(decoded)) await stopCamera();
                    processingScan = false;
                },
                () => { /* No QR detected in this frame. */ }
            );
            cameraRunning = true;
            stopButton.disabled = false;
            notice("Point the camera at a CropCare device QR label.", "info");
        } catch (_) {
            cameraRunning = false;
            startButton.disabled = false;
            stopButton.disabled = true;
            notice("Could not start camera. Allow camera permission on an HTTPS page, or upload a QR image instead.", "warning");
        }
    });

    stopButton?.addEventListener("click", async () => {
        await stopCamera();
        notice(rawInput.value ? "QR recognized. Ready to pair." : "Camera stopped. You can scan again or upload an image.", "secondary");
    });

    fileInput?.addEventListener("change", async () => {
        const file = fileInput.files?.[0];
        if (!file) return;
        if (!file.type.startsWith("image/") || file.size > 10 * 1024 * 1024) {
            notice("Select an image file smaller than 10 MB.", "warning");
            fileInput.value = "";
            return;
        }
        if (!ensureScanner()) return;
        await stopCamera();
        notice("Decoding QR image locally...", "info");
        try {
            const decoded = await scanner.scanFile(file, false);
            acceptDecoded(decoded);
        } catch (_) {
            rawInput.value = "";
            pairButton.disabled = true;
            notice("No readable QR code found in this image. Try a clearer photo.", "warning");
        }
        fileInput.value = "";
    });

    form.addEventListener("submit", event => {
        if (!parseCropCareQr(rawInput.value) || !hasZones) {
            event.preventDefault();
            notice("Scan or upload a valid CropCare QR code and select a farm zone first.", "warning");
            return;
        }
        pairButton.disabled = true;
        pairButton.textContent = "Pairing...";
    });

    // Recover the decoded QR after a server-side validation error.
    if (rawInput.value) acceptDecoded(rawInput.value);
    else pairButton.disabled = true;
    window.addEventListener("pagehide", () => { void stopCamera(); });
})();