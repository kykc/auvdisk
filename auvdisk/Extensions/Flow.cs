using auvdisk.Log;
using DotNext;
using Spectre.Console.Rendering;

namespace auvdisk.Extensions;

internal static class FlowExtensions
{
    public static Flow<TSubj> Flow<TSubj>(this Optional<TSubj> subj, string error, IFlowContextHandler? context = null) where TSubj: class
    {
        return subj.HasValue ? Flows.Val(subj.Value, context) : Flows.Err<TSubj>(error, context);
    }
    
    public static bool LogErrorIfAny<TSubj>(this Flow<TSubj> subj, ILog logger) where TSubj : class
    {
        return subj.LogErrorIfAny(logger.Error);
    }
    
    public static Flow<TSubj> LogOk<TSubj>(this Flow<TSubj> subj, ILog logger, Func<TSubj, string> msg) where TSubj : class
    {
        return subj.SideEffect(s => logger.Log(msg(s)));
    }

    public static Flow<TSubj> LogIf<TSubj>(this Flow<TSubj> subj, ILog logger, Func<TSubj, bool> condition, string msg) where TSubj : class
    {
        return subj.SideEffectIf((val, _) => condition(val), (_, _) => logger.Log(msg));
    }
    
    public static Flow<TSubj> LogIf<TSubj>(this Flow<TSubj> subj, ILog logger, Func<TSubj, bool> condition, IRenderable msg) where TSubj : class
    {
        return subj.SideEffectIf((val, _) => condition(val), (_, _) => logger.Log(msg));
    }
    
    public static Flow<TSubj> LogOk<TSubj>(this Flow<TSubj> subj, ILog logger, string msg) where TSubj : class
    {
        return subj.SideEffect(_ => logger.Log(msg));
    }
}