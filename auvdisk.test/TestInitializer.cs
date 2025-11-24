namespace auvdisk.test;

using System.Runtime.CompilerServices;

public static class TestInitializer
{
    [ModuleInitializer]
    public static void Initialize()
    {
        DiscUtils.Complete.SetupHelper.SetupComplete();
        Program.IsInteractive = false;
    }
}