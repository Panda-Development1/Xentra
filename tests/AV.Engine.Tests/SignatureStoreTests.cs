using AV.Engine.Services;
using Xunit;

namespace AV.Engine.Tests;

public class SignatureStoreTests
{
    [Fact]
    public async Task LoadSignatures_LoadsSuccessfully()
    {
        var store = new SignatureStore();
        await store.LoadSignaturesAsync();

        Assert.True(await store.ValidateIntegrityAsync());
        Assert.True(store.GetSignatureCount() > 0);
    }

    [Fact]
    public async Task LoadSignatures_ContainsEICARSignature()
    {
        var assembly = typeof(SignatureStore).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .First(n => n.EndsWith("signatures.json"));

        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        var json = await reader.ReadToEndAsync();

        Assert.Contains("EICAR-001", json);
        Assert.Contains("EICAR-Test-File", json);
    }

    [Fact]
    public async Task ValidateIntegrity_BeforeLoad_ReturnsFalse()
    {
        var store = new SignatureStore();

        Assert.False(await store.ValidateIntegrityAsync());
    }

    [Fact]
    public async Task GetSignatureCount_BeforeLoad_ReturnsZero()
    {
        var store = new SignatureStore();

        Assert.Equal(0, store.GetSignatureCount());
    }
}
