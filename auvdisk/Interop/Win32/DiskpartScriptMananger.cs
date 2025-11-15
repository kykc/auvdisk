#if WINDOWS
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using auvdisk.Extensions;
using auvdisk.Log;
using Microsoft.PowerShell;

namespace auvdisk.Interop.Win32;

public static class DiskpartScriptMananger
{
    public record ScriptOutput(string StandardOutput, string StandardError, int ExitCode);
    
    public static Flow<string> GenerateSetidScript(string partitionType, int diskNumber, int partitionNumber, ILog logger)
    {
        string partTypeGuid = "";
                
        if (partitionType == "data")
        {
            partTypeGuid = "ebd0a0a2-b9e5-4433-87c0-68b6b72699c7";
        }
        else if (partitionType == "efi")
        {
            partTypeGuid = "c12a7328-f81f-11d2-ba4b-00a0c93ec93b";
        }
        else
        {
            return Flows.Err<string>($"Unknown partition type: {partitionType}", logger);
        }
                
        string result =
            $"\"select disk {diskNumber}\", \"select partition {partitionNumber}\", \"set id={partTypeGuid} override\" | diskpart";
        
        return Flows.Ok(result, logger);
    }

    public static Flow<ScriptOutput> Execute(string script, ILog logger)
    {
        var state = InitialSessionState.CreateDefault();
        state.ExecutionPolicy = ExecutionPolicy.Bypass;

        using var ps = PowerShell.Create(state);
        ps.AddScript(script);

        try
        {
            logger.Log("Executing diskpart script...");
            logger.Log(script);
            var result = ps.Invoke();

            var stdOut = string.Join(Environment.NewLine, result.Select(x => x.BaseObject.ToString()));
            var stdErr = string.Join(Environment.NewLine, ps.Streams.Error.Select(x => x.ToString()));
            
            var lastExitCode = ps.Runspace.SessionStateProxy.GetVariable("LASTEXITCODE").ParseInt();

            if (lastExitCode is not 0)
            {
                logger.Debug(stdOut);
                
                stdErr = $"diskpart exited with non-zero exit code <{lastExitCode}>{Environment.NewLine}{stdErr}";
                return Flows.Err<ScriptOutput>(stdErr, logger);
            }
            
            return Flows.Ok(new ScriptOutput(stdOut, stdErr, lastExitCode.Value), logger);
        }
        catch (Exception e)
        {
            return Flows.Err<ScriptOutput>(e.Message, logger);
        }
    }

    public static int? ParseInt(this object obj)
    {
        return Int32.TryParse(obj?.ToString() ?? "", out var result) ? result : null;
    }
}
#endif