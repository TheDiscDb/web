window.directoryPicker = {
    isSupported: function () {
        return typeof window.showDirectoryPicker === 'function';
    },
    openDirectory: async function () {
        var dirHandle = await window.showDirectoryPicker({ mode: 'read' });
        return await this._readDirectory(dirHandle, dirHandle.name);
    },
    _readDirectory: async function (dirHandle, basePath) {
        var results = [];
        for await (var entry of dirHandle.values()) {
            var entryPath = basePath + '/' + entry.name;
            if (entry.kind === 'file') {
                var file = await entry.getFile();
                results.push({
                    name: file.name,
                    size: file.size,
                    lastModified: file.lastModified,
                    webkitRelativePath: entryPath
                });
            } else if (entry.kind === 'directory') {
                var subResults = await this._readDirectory(entry, entryPath);
                for (var i = 0; i < subResults.length; i++) {
                    results.push(subResults[i]);
                }
            }
        }
        return results;
    },
    clickElement: function (elementId) {
        var el = document.getElementById(elementId);
        if (el) el.click();
    },
    getDirectoryFiles: function (elementId) {
        var inputElement = document.getElementById(elementId);
        if (!inputElement) return [];
        var files = inputElement.files;
        if (!files || files.length === 0) {
            return [];
        }

        var result = [];
        for (var i = 0; i < files.length; i++) {
            var file = files[i];
            result.push({
                name: file.name,
                size: file.size,
                lastModified: file.lastModified,
                webkitRelativePath: file.webkitRelativePath || ''
            });
        }
        return result;
    }
};
