using auvdisk.Log;

namespace auvdisk.Dbg;

public static class Notepad
{
    public static Flow<None> EntryPoint(Cli.Notepad rawOpts, ILog logger)
    {
        var result = Flows.Val(rawOpts);

        return result.Map(_ => None.Value);
    }
}