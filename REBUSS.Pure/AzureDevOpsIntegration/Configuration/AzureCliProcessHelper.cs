using System.Runtime.InteropServices;

namespace REBUSS.Pure.AzureDevOpsIntegration.Configuration;

/// <summary>
/// Resolves the correct <see cref="System.Diagnostics.ProcessStartInfo"/> file name and
/// arguments for running Azure CLI commands across platforms.
/// <para>
/// On Windows, <c>az</c> is a <c>.cmd</c> batch file. When <c>UseShellExecute</c> is
/// <c>false</c>, the OS does not resolve <c>.cmd</c> extensions automatically, so the
/// command must be invoked through <c>cmd.exe /c az ...</c>.
/// On Linux/macOS, <c>az</c> is a regular executable and can be called directly.
/// </para>
/// </summary>
internal static class AzureCliProcessHelper
{
    /// <summary>
    /// Returns the file name and arguments needed to run an Azure CLI command.
    /// </summary>
    /// <param name="azArguments">The Azure CLI arguments (e.g. <c>"login"</c> or
    /// <c>"account get-access-token --resource ... --output json"</c>).</param>
    /// <returns>A tuple of (<c>FileName</c>, <c>Arguments</c>) suitable for
    /// <see cref="System.Diagnostics.ProcessStartInfo"/>.</returns>
    internal static (string FileName, string Arguments) GetProcessStartArgs(string azArguments)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return ("cmd.exe", $"/c az {azArguments}");

        return ("az", azArguments);
    }
}
