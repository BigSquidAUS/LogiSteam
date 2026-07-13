using LogiSteam.Common.UsbipInterop;

namespace LogiSteam.Common.Tests;

public class ProcessRunnerTests
{
    [Fact]
    public void BuildElevatedBatchCommand_JoinsMultipleCommandsWithAnd()
    {
        string[][] argumentSets =
        [
            ["policy", "add", "--hardware-id", "046d:c24f"],
            ["policy", "add", "--hardware-id", "046d:c260"],
        ];

        var (fileName, args) = ProcessRunner.BuildElevatedBatchCommand("usbipd", argumentSets);

        Assert.Equal("cmd.exe", fileName);
        Assert.Equal("/c", args[0]);
        Assert.Equal(
            "usbipd policy add --hardware-id 046d:c24f && usbipd policy add --hardware-id 046d:c260",
            args[1]);
    }

    [Fact]
    public void BuildElevatedBatchCommand_QuotesArgumentsContainingSpaces()
    {
        string[][] argumentSets = [["some arg with spaces", "noSpaces"]];

        var (_, args) = ProcessRunner.BuildElevatedBatchCommand("my app.exe", argumentSets);

        Assert.Equal("\"my app.exe\" \"some arg with spaces\" noSpaces", args[1]);
    }

    [Fact]
    public void BuildElevatedBatchCommand_HandlesSingleCommand()
    {
        string[][] argumentSets = [["policy", "list"]];

        var (_, args) = ProcessRunner.BuildElevatedBatchCommand("usbipd", argumentSets);

        Assert.Equal("usbipd policy list", args[1]);
        Assert.DoesNotContain("&&", args[1]);
    }

    [Fact]
    public void BuildElevatedBatchCommand_HandlesEmptyArgumentSets()
    {
        var (_, args) = ProcessRunner.BuildElevatedBatchCommand("usbipd", []);

        Assert.Equal(string.Empty, args[1]);
    }
}
