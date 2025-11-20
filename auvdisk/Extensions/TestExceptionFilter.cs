namespace auvdisk.Extensions;

using System;

public static class TestExceptionFilter
{
    public static bool ShouldCatch(Exception ex)
    {
        if (ex.GetType().Namespace?.StartsWith("Xunit.Sdk") == true)
        {
            return false; // DO NOT CATCH - let the assertion propagate
        }
        
        if (ex.GetType().Assembly.GetName().Name?.StartsWith("xunit") == true)
        {
            return false; // DO NOT CATCH - let the assertion propagate
        }
        
        return true; 
    }
}