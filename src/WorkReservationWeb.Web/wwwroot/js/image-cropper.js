// Opens a modal with Cropper.js over the given image data URL and resolves with the
// cropped JPEG as a base64 string (without the data: prefix), or null when cancelled.
window.workReservationImageCropper = {
    show: function (imageDataUrl) {
        return new Promise(function (resolve) {
            const backdrop = document.createElement("div");
            backdrop.className = "wr-modal-backdrop";
            backdrop.innerHTML =
                '<div class="wr-modal">' +
                '  <div class="wr-modal-title">Crop image</div>' +
                '  <div class="wr-modal-canvas"><img alt="Image to crop" /></div>' +
                '  <div class="wr-modal-actions">' +
                '    <button type="button" class="btn btn-outline-secondary" data-action="cancel">Cancel</button>' +
                '    <button type="button" class="btn btn-primary" data-action="confirm">Use cropped image</button>' +
                '  </div>' +
                '</div>';

            const image = backdrop.querySelector("img");
            let cropper = null;

            function close(result) {
                if (cropper) {
                    cropper.destroy();
                }
                backdrop.remove();
                resolve(result);
            }

            backdrop.querySelector('[data-action="cancel"]').addEventListener("click", function () {
                close(null);
            });

            backdrop.querySelector('[data-action="confirm"]').addEventListener("click", function () {
                if (!cropper) {
                    close(null);
                    return;
                }

                const canvas = cropper.getCroppedCanvas({
                    maxWidth: 1280,
                    imageSmoothingEnabled: true,
                    imageSmoothingQuality: "high"
                });
                const dataUrl = canvas.toDataURL("image/jpeg", 0.85);
                close(dataUrl.substring(dataUrl.indexOf(",") + 1));
            });

            image.addEventListener("load", function () {
                cropper = new Cropper(image, {
                    aspectRatio: 16 / 9,
                    viewMode: 1,
                    autoCropArea: 1,
                    responsive: true
                });
            }, { once: true });

            document.body.appendChild(backdrop);
            image.src = imageDataUrl;
        });
    }
};
