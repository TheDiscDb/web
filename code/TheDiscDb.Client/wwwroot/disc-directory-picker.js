const selections = new Map();

export function supportsFileSystemAccess() {
    return typeof window.showDirectoryPicker === "function";
}

export function pickDirectory(maxFileCount) {
    return new Promise((resolve, reject) => {
        const input = document.createElement("input");
        input.type = "file";
        input.multiple = true;
        input.webkitdirectory = true;
        input.hidden = true;
        document.body.appendChild(input);

        let settled = false;
        const finish = (value) => {
            if (settled) {
                return;
            }

            settled = true;
            input.remove();
            resolve(value);
        };

        input.addEventListener("cancel", () => finish(null), { once: true });
        input.addEventListener("change", () => {
            try {
                const files = [...input.files];
                if (files.length === 0) {
                    finish(null);
                    return;
                }

                if (files.length > maxFileCount) {
                    throw new Error(
                        `The selected directory contains ${files.length} files. The maximum supported count is ${maxFileCount}.`);
                }

                const selectionId = crypto.randomUUID();
                const selectedFiles = new Map();
                const metadata = files.map((file, index) => {
                    const id = index.toString();
                    selectedFiles.set(id, file);
                    return {
                        id,
                        name: file.name,
                        relativePath: file.webkitRelativePath,
                        size: file.size,
                        lastModified: file.lastModified,
                    };
                });

                selections.set(selectionId, selectedFiles);
                finish({ selectionId, files: metadata });
            } catch (error) {
                settled = true;
                input.remove();
                reject(error);
            }
        }, { once: true });

        input.click();
    });
}

export async function readFile(selectionId, fileId, maxAllowedSize) {
    const file = selections.get(selectionId)?.get(fileId);
    if (!file) {
        throw new Error("The selected file is no longer available.");
    }

    if (file.size > maxAllowedSize) {
        throw new Error(
            `${file.name} exceeds the ${maxAllowedSize}-byte read limit.`);
    }

    return new Uint8Array(await file.arrayBuffer());
}

export function releaseSelection(selectionId) {
    selections.delete(selectionId);
}

export function downloadBytes(fileName, contentType, contents) {
    const blob = new Blob([contents], { type: contentType });
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement("a");
    anchor.href = url;
    anchor.download = fileName;
    anchor.click();
    URL.revokeObjectURL(url);
}
