// This is a workaround until this issue gets fixed: https://github.com/dotnet/aspnetcore/issues/64009
export function onRuntimeConfigLoaded(config) {
    config.disableNoCacheFetch = true;
}