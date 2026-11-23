using System.Reflection;
using System.Text.Json;
using KristofferStrube.Blazor.FileSystemAccess;
using Microsoft.JSInterop;
using TheDiscDb.Client.Interop;
using TheDiscDb.Client.Services;

namespace TheDiscDb.UnitTests.Client;

public class DiscDirectoryPickerTests
{
    [Test]
    [Arguments(true, true)]
    [Arguments(false, true)]
    [Arguments(false, false)]
    public async Task PickAsync_CompatibilitySelection_PreservesFilesAndReleasesSelection(
        bool supportsNativePicker, bool useCompatibilityPicker)
    {
        var js = new PickerJsRuntime(supportsNativePicker);
        var native = DispatchProxy.Create<IFileSystemAccessServiceInProcess, NativePickerProxy>();
        await using var picker = new DiscDirectoryPicker(js, native);
        await picker.PreloadAsync();
        int committed = 0;
        int nativeAborted = 0;

        var selection = await picker.PickAsync(
            onSelectionCommitted: () => committed++,
            useCompatibilityPicker: useCompatibilityPicker,
            onNativePickerAborted: () => nativeAborted++);

        await Assert.That(selection).IsNotNull();
        await Assert.That(((NativePickerProxy)native).Calls).IsEqualTo(0);
        await Assert.That(committed).IsEqualTo(1);
        await Assert.That(nativeAborted).IsEqualTo(0);
        await Assert.That(selection!.Files.Count).IsEqualTo(1);
        await Assert.That(selection.Files[0].Path).IsEqualTo("BDMV/index.bdmv");
        await Assert.That(await selection.Files[0].ReadBytesAsync(100)).IsEquivalentTo(new byte[] { 1, 2, 3 });
        await selection.DisposeAsync();
        await Assert.That(js.Released).IsTrue();
    }

    [Test]
    public async Task PickAsync_DefaultStillUsesNativePickerAfterCompatibilityCancellation()
    {
        var js = new PickerJsRuntime(true) { CancelSelection = true };
        var native = DispatchProxy.Create<IFileSystemAccessServiceInProcess, NativePickerProxy>();
        await using var picker = new DiscDirectoryPicker(js, native);
        await picker.PreloadAsync();
        int committed = 0;
        int nativeAborted = 0;

        var compatibilitySelection = await picker.PickAsync(
            onSelectionCommitted: () => committed++, useCompatibilityPicker: true,
            onNativePickerAborted: () => nativeAborted++);
        await Assert.That(nativeAborted).IsEqualTo(0);

        var nativeSelection = await picker.PickAsync(
            onSelectionCommitted: () => committed++,
            onNativePickerAborted: () => nativeAborted++);

        await Assert.That(compatibilitySelection).IsNull();
        await Assert.That(nativeSelection).IsNull();
        await Assert.That(js.PickerCalls).IsEqualTo(1);
        await Assert.That(((NativePickerProxy)native).Calls).IsEqualTo(1);
        await Assert.That(committed).IsEqualTo(0);
        await Assert.That(nativeAborted).IsEqualTo(1);
        await Assert.That(js.Released).IsFalse();
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task ScanUploader_PickerCancellation_OffersAlternativeOnlyForNativePicker(
        bool saveDraft, bool useCompatibilityPicker)
    {
        var js = new PickerJsRuntime(true) { CancelSelection = true };
        var native = DispatchProxy.Create<IFileSystemAccessServiceInProcess, NativePickerProxy>();
        await using var picker = new DiscDirectoryPicker(js, native);
        using var httpClient = new HttpClient();
        var uploader = new DiscScanUploader(picker, null!, httpClient);
        await uploader.PreloadAsync(CancellationToken.None);
        int nativeAborted = 0;

        var result = saveDraft
            ? (await uploader.ScanAndSaveDraftAsync(
                "https://localhost/", () => { }, _ => { }, CancellationToken.None,
                useCompatibilityPicker: useCompatibilityPicker,
                onNativePickerAborted: () => nativeAborted++)).Result
            : await uploader.ScanAndUploadAsync(
                "https://localhost/", () => { }, _ => { }, CancellationToken.None,
                useCompatibilityPicker: useCompatibilityPicker,
                onNativePickerAborted: () => nativeAborted++);

        await Assert.That(result.Cancelled).IsTrue();
        await Assert.That(result.Uploaded).IsFalse();
        await Assert.That(js.PickerCalls).IsEqualTo(useCompatibilityPicker ? 1 : 0);
        await Assert.That(((NativePickerProxy)native).Calls).IsEqualTo(useCompatibilityPicker ? 0 : 1);
        await Assert.That(nativeAborted).IsEqualTo(useCompatibilityPicker ? 0 : 1);
    }

    public class NativePickerProxy : DispatchProxy
    {
        public int Calls { get; private set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            this.Calls++;
            throw new JSException("AbortError: The user cancelled the picker.");
        }
    }

    private sealed class PickerJsRuntime(bool supportsNativePicker) : IJSRuntime, IJSObjectReference
    {
        public bool CancelSelection { get; init; }
        public bool Released { get; private set; }
        public int PickerCalls { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            object? result = null;
            switch (identifier)
            {
                case "import":
                    result = this;
                    break;
                case "supportsFileSystemAccess":
                    result = supportsNativePicker;
                    break;
                case "pickDirectory":
                    this.PickerCalls++;
                    if (!this.CancelSelection)
                    {
                        result = JsonSerializer.Deserialize<TValue>(
                            """
                            {"selectionId":"test-selection","files":[
                              {"id":"0","name":"index.bdmv","relativePath":"DISC/BDMV/index.bdmv","size":3,"lastModified":0}
                            ]}
                            """, JsonSerializerOptions.Web);
                    }
                    break;
                case "readFile":
                    result = new byte[] { 1, 2, 3 };
                    break;
                case "releaseSelection":
                    this.Released = true;
                    break;
                default:
                    throw new InvalidOperationException($"Unexpected JS call: {identifier}");
            }

            return ValueTask.FromResult(result is null ? default! : (TValue)result);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}