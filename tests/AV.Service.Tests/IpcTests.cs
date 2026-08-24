using AV.Service.Ipc;
using Xunit;

namespace AV.Service.Tests;

public class IpcTests
{
    [Fact]
    public void IpcCommandsHaveExpectedValues()
    {
        Assert.Equal("GET_STATUS", IpcCommands.GET_STATUS);
        Assert.Equal("START_SCAN", IpcCommands.START_SCAN);
        Assert.Equal("GET_QUARANTINE_LIST", IpcCommands.GET_QUARANTINE_LIST);
        Assert.Equal("RESTORE_FILE", IpcCommands.RESTORE_FILE);
        Assert.Equal("DELETE_FILE", IpcCommands.DELETE_FILE);
    }
}
