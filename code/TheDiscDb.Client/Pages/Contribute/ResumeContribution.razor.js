const databaseName = "thediscdb-scans";

async function openDatabase() {
    return await new Promise((resolve, reject) => {
        const request = indexedDB.open(databaseName, 1);
        request.onupgradeneeded = () => {
            request.result.createObjectStore("scans");
            request.result.createObjectStore("continuations");
        };
        request.onsuccess = () => resolve(request.result);
        request.onerror = () => reject(request.error);
        request.onblocked = () => reject(new Error("Close other contribution tabs and retry."));
    });
}

async function transact(storeName, mode, action) {
    const database = await openDatabase();
    try {
        return await new Promise((resolve, reject) => {
            const transaction = database.transaction(storeName, mode);
            const request = action(transaction.objectStore(storeName));
            let result;
            request.onsuccess = () => { result = request.result; };
            transaction.oncomplete = () => resolve(result ?? null);
            transaction.onerror = () => reject(transaction.error);
            transaction.onabort = () => reject(transaction.error ?? new Error("Scan storage was interrupted."));
        });
    } finally {
        database.close();
    }
}

export async function get(store, key) {
    return await transact(store, "readonly", objectStore => objectStore.get(key));
}

export async function set(store, key, json) {
    await transact(store, "readwrite", objectStore => objectStore.put(json, key));
}

export async function removeScan(hash, expectedId) {
    await transact("scans", "readwrite", objectStore => {
        const request = objectStore.get(hash);
        request.addEventListener("success", () => {
            if (request.result && JSON.parse(request.result).Id === expectedId) {
                objectStore.delete(hash);
            }
        });
        return request;
    });
}
