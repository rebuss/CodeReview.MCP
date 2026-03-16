using System.Runtime.InteropServices;
using REBUSS.Pure.AzureDevOpsIntegration.Configuration;

namespace REBUSS.Pure.Tests.AzureDevOpsIntegration;

public class AzureCliProcessHelperTests
{
    [Fact]
    public void GetProcessStartArgs_ReturnsAzArguments()
    {
        var (fileName, arguments) = AzureCliProcessHelper.GetProcessStartArgs("account get-access-token --output json");

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Equal("cmd.exe", fileName);
            Assert.Equal("/c az account get-access-token --output json", arguments);
        }
        else
        {
            Assert.Equal("az", fileName);
            Assert.Equal("account get-access-token --output json", arguments);
        }
    }

    [Fact]
    public void GetProcessStartArgs_Login_ReturnsCorrectFormat()
    {
        var (fileName, arguments) = AzureCliProcessHelper.GetProcessStartArgs("login");

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Equal("cmd.exe", fileName);
            Assert.Equal("/c az login", arguments);
        }
        else
        {
            Assert.Equal("az", fileName);
            Assert.Equal("login", arguments);
        }
    }
}
