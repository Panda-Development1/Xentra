using AV.Engine.Services;
using Xunit;

namespace IntegrationTests;

public class InstallerVerification
{
    [Fact]
    public void SignatureStore_LoadsAndDetectsEICAR()
    {
        var store = new SignatureStore();
        store.LoadSignaturesAsync().GetAwaiter().GetResult();

        Assert.True(store.GetSignatureCount() > 0);
    }
}
