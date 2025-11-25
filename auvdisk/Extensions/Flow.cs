using System.Diagnostics;
using auvdisk.Log;
using DotNext;

namespace auvdisk.Extensions
{
    public class Value<TSubj>(TSubj value)
        where TSubj : struct
    {
        public TSubj Val { get; private set; } = value;
    }

    public class None
    {
        private None()
        {
        }

        public static readonly None Value = new();
    }

    public static class Flows
    {
        public static Flow<TSubj> Optional<TSubj>(Optional<TSubj> value, string error, IFlowContextHandler? context = null) where TSubj: class
        {
            return value.HasValue ? Val(value.Value, context) : Err<TSubj>(error, context);
        }
        
        public static Flow<TSubj> Val<TSubj>(TSubj value, IFlowContextHandler? context = null) where TSubj: class
        {
            return Flow<TSubj>.Val(value, new FlowContext(context));
        }

        public static Flow<TSubj> ValOr<TSubj>(TSubj? value, string error, IFlowContextHandler? context = null) where TSubj : class
        {
            return value != null ? Val(value, context) : Err<TSubj>(error, context);
        }
        
        public static Flow<TSubj> Err<TSubj>(string error, IFlowContextHandler? context = null) where TSubj: class
        {
            return Flow<TSubj>.Err(error, new FlowContext(context));
        }

        public static Flow<Value<TSubj>> RefVal<TSubj>(TSubj value, IFlowContextHandler? context = null) where TSubj : struct
        {
            return Val(value.RefVal(), context);
        }
    }
    
    public static class FlowExtensions
    {
        public static Flow<TSubj> Flow<TSubj>(this Optional<TSubj> subj, string error, IFlowContextHandler? context = null) where TSubj: class
        {
            return subj.HasValue ? Flows.Val(subj.Value, context) : Flows.Err<TSubj>(error, context);
        }

        public static Flow<TSubj> Flow<TSubj>(this TSubj? subj, string error, IFlowContextHandler? context = null) where TSubj : class
        {
            return Flows.ValOr(subj, error, context);
        }
        
        public static Flow<Value<TSubj>> Flow<TSubj>(this TSubj? subj, string error, IFlowContextHandler? context = null) where TSubj : struct
        {
            return subj.HasValue ? Flows.Val(new Value<TSubj>(subj.Value), context) : Flows.Err<Value<TSubj>>(error, context);
        }

        public static Flow<TSubj> Flow<TSubj>(this TSubj subj, IFlowContextHandler? context = null) where TSubj : class
        {
            return Flows.Val(subj, context);
        }

        public static Flow<None> Flow(this bool value, string error, IFlowContextHandler? context = null)
        {
            return value ? Flows.Val(None.Value, context) : Flows.Err<None>(error, context); 
        }
    }

    public static class FlowOperations
    {
        public static Flow<TRes> MapDispose<TSubj, TRes>(this Flow<TSubj> subj, Func<TSubj, TRes> mapper)
            where TRes : class
            where TSubj : class, IDisposable
        {
            return subj.MapDispose(mapper, _ => subj.UnwrapVal());
        }

        public static Flow<TRes> MapDispose<TSubj, TDisposable, TRes>(this Flow<TSubj> subj, Func<TSubj, TRes> mapper, Func<TSubj, TDisposable> disposable)
            where TRes : class
            where TSubj : class
            where TDisposable : IDisposable
        {
            try
            {
                if (subj.IsVal)
                {
                    var val = mapper(subj.UnwrapVal());
                    var disposableInst = disposable(subj.UnwrapVal());
                    disposableInst.Dispose();
                    subj.Context.RemoveDisposable(disposableInst);

                    return Flow<TRes>.Val(val, subj.Context);
                }
                else
                {
                    return new(subj.UnwrapErr(), subj.Context);
                }
            }
            catch (Exception ex) when (subj.Context.ShouldCatch(ex))
            {
                return new(subj.Context.ToErrorString(ex).Item1, subj.Context);
            }
        }
        
        public static Flow<TRes> BindConcat<TSubj, TNew, TRes>(this Flow<TSubj> subj, Func<TSubj, Flow<TNew>> binder, Func<TSubj, TNew, TRes> converter, bool changeContext = false)
            where TRes : class
            where TNew: class
            where TSubj: class
        {
            Flow<TNew> BinderWrapper(TSubj s, FlowContext ctx) => binder(s);

            return subj.BindConcat(BinderWrapper, converter, changeContext);
        }
        
        public static Flow<TRes> BindConcat<TSubj, TNew, TRes>(this Flow<TSubj> subj, Func<TSubj, FlowContext, Flow<TNew>> binder, Func<TSubj, TNew, TRes> converter, bool changeContext = false)
            where TRes : class
            where TNew: class
            where TSubj: class
        {
            try
            {
                if (subj.IsVal)
                {
                    var newVal = binder(subj.UnwrapVal(), subj.Context);

                    if (newVal.IsVal)
                    {
                        var ctx = changeContext ? newVal.Context : subj.Context;
                        var prevCtx = changeContext ? subj.Context : newVal.Context;
                        
                        prevCtx.GetDisposables().ForEach(x => ctx.AddDisposable(x));
                        prevCtx.GetDisposables().ToList().ForEach(x => prevCtx.RemoveDisposable(x));
                        
                        return Flow<TRes>.Val(converter(subj.UnwrapVal(), newVal.UnwrapVal()), ctx);
                    }
                    else
                    {
                        return new(newVal.UnwrapErr(), changeContext ? newVal.Context : subj.Context);
                    }
                }
                else
                {
                    return new(subj.UnwrapErr(), subj.Context);
                }
            }
            catch (Exception ex) when (subj.Context.ShouldCatch(ex))
            {
                return new(subj.Context.ToErrorString(ex).Item1, subj.Context);
            }
        }
        
        public static Flow<TRes> MapConcat<TSubj, TNew, TRes>(this Flow<TSubj> subj, Func<TSubj, TNew> transformer, Func<TSubj, TNew, TRes> converter)
            where TRes : class
            where TNew: class
            where TSubj: class
        {
            try
            {
                if (subj.IsVal)
                {
                    var newVal = transformer(subj.UnwrapVal());

                    // This value will not be seen by Flow constructor, need to check explicitly here
                    if (newVal is IDisposable disposable)
                    {
                        subj.Context.AddDisposable(disposable);
                    }
                    
                    return Flow<TRes>.Val(converter(subj.UnwrapVal(), newVal), subj.Context);
                }
                else
                {
                    return new(subj.UnwrapErr(), subj.Context);
                }
            }
            catch (Exception ex) when (subj.Context.ShouldCatch(ex))
            {
                return new(subj.Context.ToErrorString(ex).Item1, subj.Context);
            }
        }

        public static Flow<TSubj> SideEffectIf<TSubj>(this Flow<TSubj> subj, Func<TSubj, bool> condition, Action<TSubj> action)
            where TSubj : class
        {
            try
            {
                if (subj.IsVal && condition(subj.UnwrapVal()))
                {
                    action(subj.UnwrapVal());
                }

                return subj;
            }
            catch (Exception ex) when (subj.Context.ShouldCatch(ex))
            {
                return new(subj.Context.ToErrorString(ex).Item1, subj.Context);
            }
        }

        public static Flow<TSubj> SideEffect<TSubj>(this Flow<TSubj> subj, Action<TSubj> action) where TSubj : class
        {
            return subj.SideEffectIf(_ => true, action);
        }

        public static Flow<TSubj> GetContext<TSubj>(this Flow<TSubj> subj, Action<FlowContext> action) where TSubj : class
        {
            action(subj.Context);

            return subj;
        }
        
        public static Flow<TRes> Bind<TSubj, TRes>(this Flow<TSubj> subj, Func<TSubj, Flow<TRes>> binder, bool switchContext = false)
            where TSubj : class
            where TRes : class
        {
            return subj.BindConcat(binder, (_, right) => right, switchContext);
        }
        
        public static Flow<TRes> Bind<TSubj, TRes>(this Flow<TSubj> subj, Func<TSubj, FlowContext, Flow<TRes>> binder, bool switchContext = false)
            where TSubj : class
            where TRes : class
        {
            return subj.BindConcat(binder, (_, right) => right, switchContext);
        }

        public static Flow<TSubj> LogOk<TSubj>(this Flow<TSubj> subj, ILog logger, string msg) where TSubj : class
        {
            return subj.SideEffect(_ => logger.Log(msg));
        }

        public static Flow<TSubj> Err<TSubj>(this Flow<TSubj> subj, string error) where TSubj : class
        {
            return new(error, subj.Context);
        }
        
        public static Flow<TSubj> LogOk<TSubj>(this Flow<TSubj> subj, ILog logger, Func<TSubj, string> msg) where TSubj : class
        {
            return subj.SideEffect(s => logger.Log(msg(s)));
        }

        public static Flow<TSubj> LogIf<TSubj>(this Flow<TSubj> subj, ILog logger, Func<TSubj, bool> condition, string msg) where TSubj : class
        {
            return subj.SideEffectIf(condition, _ => logger.Log(msg));
        }

        public static Flow<TSubj> Check<TSubj>(this Flow<TSubj> subj, Func<TSubj, bool> predicate, Func<TSubj, string> error) where TSubj : class
        {
            return subj.CheckIf(_ => true, predicate, error);
        }

        public static Flow<TSubj> CheckIf<TSubj>(this Flow<TSubj> subj, Func<TSubj, bool> condition, Func<TSubj, bool> predicate, Func<TSubj, string> error)
            where TSubj : class
        {
            Flow<TSubj> Binder(TSubj s) => predicate(s) ? subj : new(error(s), subj.Context);

            return subj.BindErrIf(condition, Binder);
        }
        
        public static Flow<TRes> MapOr<TSubj, TRes>(this Flow<TSubj> subj, Func<TSubj, TRes?> mapper, string error)
            where TSubj : class
            where TRes : class
        {
            try
            {
                if (subj.IsVal && mapper(subj.UnwrapVal()) is { } newValue)
                {
                    return Flow<TRes>.Val(newValue, subj.Context);
                }
                else
                {
                    return new(subj.IsErr ? subj.UnwrapErr() : error, subj.Context);
                }
            }
            catch (Exception ex) when (subj.Context.ShouldCatch(ex))
            {
                return new(subj.Context.ToErrorString(ex).Item1, subj.Context);
            }
        }

        public static Flow<TRes> Map<TSubj, TRes>(this Flow<TSubj> subj, Func<TSubj, TRes> mapper)
            where TSubj : class
            where TRes : class
        {
            return subj.MapConcat(mapper, (_, newVal) => newVal);
        }
        
        public static Flow<TSubj> BindErrIf<TSubj, TOther>(this Flow<TSubj> subj, Func<TSubj, bool> condition, Func<TSubj, Flow<TOther>> binder, bool switchContext = false) 
            where TSubj : class
            where TOther : class
        {
            Flow<TOther> BinderAdapter(TSubj s, FlowContext ctx) => binder(s);

            return subj.BindErrIf(condition, BinderAdapter, switchContext);
        }
        
        public static Flow<TSubj> BindErrIf<TSubj, TOther>(this Flow<TSubj> subj, Func<TSubj, bool> condition, Func<TSubj, FlowContext, Flow<TOther>> binder, bool switchContext = false) 
            where TSubj : class
            where TOther : class
        {
            try
            {
                if (!subj.IsVal || !condition(subj.UnwrapVal())) return subj;

                return subj.BindConcat(binder, (left, _) => left, switchContext);
            }
            catch (Exception ex) when (subj.Context.ShouldCatch(ex))
            {
                return new(subj.Context.ToErrorString(ex).Item1, subj.Context);
            }
        }
        
        public static Flow<TSubj> BindErr<TSubj, TOther>(this Flow<TSubj> subj, Func<TSubj, Flow<TOther>> binder, bool switchContext = false) 
            where TSubj : class
            where TOther : class
        {
            return subj.BindConcat(binder, (left, _) => left, switchContext);
        }
        
        public static Flow<TSubj> BindErr<TSubj, TOther>(this Flow<TSubj> subj, Func<TSubj, FlowContext, Flow<TOther>> binder, bool switchContext = false) 
            where TSubj : class
            where TOther : class
        {
            return subj.BindConcat(binder, (left, _) => left, switchContext);
        }
        
        public static Flow<TSubj> Finally<TSubj>(this Flow<TSubj> subj, Action<FlowContext> action) where TSubj : class
        {
            try
            {
                action(subj.Context);
                return subj;
            }
            catch (Exception ex) when (subj.Context.ShouldCatch(ex))
            {
                return new(subj.Context.ToErrorString(ex).Item1, subj.Context);
            }
        }
        
        public static bool LogErrorIfAny<TSubj>(this Flow<TSubj> subj, ILog logger) where TSubj : class
        {
            if (!subj.IsErr) return false;
            
            logger.Error(subj.UnwrapErr());

            return true;
        }
    }
    
    public sealed class Flow<TSubj> : IDisposable
        where TSubj : class
    {
        private readonly TSubj? _value;
        private readonly string? _error;
        public bool IsErr => _error != null;
        public bool IsVal => _value != null;
        public FlowContext Context { get; }

        // This allows to construct error Flow w/o naming its type parameters explicitly
        public Flow(string error, FlowContext? context = null)
        {
#if DEBUG
            // All the operations on Flow should always pass the context explicitly
            // I found no better way to guard myself from making an easy mistake of omitting the context somewhere
            if (!context.IsSome())
            {
                var stackTrace = new StackTrace();
                var frame = stackTrace.GetFrame(1);

                if (frame != null)
                {
                    var method = frame.GetMethod();
                    var callingClass = method?.DeclaringType;

                    Debug.Assert(!(callingClass?.FullName?.StartsWith("auvdisk.Extensions.Flow") ?? false), "Flow constructed w/o context internally");
                }
            }
#endif
            _value = null;
            _error = error;
            Context = context ?? new FlowContext();
        }

        private Flow(TSubj? value, string? error, FlowContext context)
        {
            Debug.Assert(value != null || error != null, "value != null || error != null");
            
            _value = value;
            _error = error;
            Context = context;
        }

        public static Flow<TSubj> Val(TSubj value, FlowContext context)
        {
            if (value is IDisposable disposable)
            {
                context.AddDisposable(disposable);
            }
            
            return new Flow<TSubj>(value, null, context);
        }
        
        public static Flow<TSubj> Err(string error, FlowContext context)
        {
            return new Flow<TSubj>(null, error, context);
        }

        public Flow<TSubj> WithHandler(IFlowContextHandler contextHandler)
        {
            Context.With(contextHandler);
            
            return this;
        }

        public Flow<TSubj> Handle<TEx>(Func<TEx, string> handleString) where TEx : Exception
        {
            return WithHandler(FlowContextHandler.Create(handleString));
        }

        public Flow<TSubj> PopHandler()
        {
            Context.Pop();

            return this;
        }

        public Flow<TSubj> HandleAll()
        {
            return WithHandler(FlowContextHandler.Create(_ => true));
        }

        public TSubj UnwrapVal()
        {
            return _value ?? throw new NullReferenceException();
        }

        public string UnwrapErr()
        {
            return _error ?? throw new NullReferenceException();
        }

        public void Dispose()
        {
            foreach (var disposable in Context.GetDisposables().Distinct().ToList())
            {
                try
                {
                    disposable.Dispose();
                    Context.RemoveDisposable(disposable);
                }
                catch (Exception e) when (Program.ExceptionFilter(e))
                {
                    // ignore exceptions on dispose
                    Debug.WriteLine($"Exception on Flow.Dispose when disposing {disposable.GetType()}");
                    Debug.WriteLine($"Exception {e.GetType()}: {e.Message}");
                }
            }
        }
    }

    public interface IFlowContextHandler
    {
        Func<Exception, bool> ShouldCatch { get; }
        Func<Exception, (string, bool)> ToErrorString { get; }
    }

    public class FlowContext
    {
        private readonly LinkedList<IFlowContextHandler> _handlers = [];
        private readonly HashSet<IDisposable> _disposables = [];
        
        public Func<Exception, bool> ShouldCatch => ShouldCatchImpl;
        public Func<Exception, (string, bool)> ToErrorString => ToErrorStringImpl;
        
        // For UTs
        internal IEnumerable<IFlowContextHandler> Handlers => _handlers;

        public FlowContext(IFlowContextHandler? context = null)
        {
            if (context != null) _handlers.AddFirst(context);
        }
        
        public void AddDisposable(IDisposable disposable)
        {
            _disposables.Add(disposable);
        }

        public void RemoveDisposable(IDisposable disposable)
        {
            _disposables.Remove(disposable);
        }

        public IEnumerable<IDisposable> GetDisposables()
        {
            return _disposables;
        }

        public void With(IFlowContextHandler contextHandler)
        {
            _handlers.AddFirst(contextHandler);
        }

        public void Pop()
        {
            _handlers.RemoveFirst();
        }

        internal void ClearHandlers()
        {
            _handlers.Clear();
        }
        
        private (string, bool) ToErrorStringImpl(Exception ex)
        {
            foreach (var other in _handlers)
            {
                if (other.ToErrorString(ex) is (var str, true))
                {
                    return (str, true);
                }
            }
            
            return (ex.Message, false);
        }

        private bool ShouldCatchImpl(Exception ex)
        {
            return _handlers.Any(x => x.ShouldCatch(ex));
        }
    }
    
    class FlowContextHandler : IFlowContextHandler
    {
        private readonly (Type, Delegate) _toString;
        private readonly Func<Exception, bool> _filter;

        public Func<Exception, bool> ShouldCatch => ShouldCatchImpl;
        public Func<Exception, (string, bool)> ToErrorString => ToErrorStringImpl;

        private FlowContextHandler(Func<Exception, bool> filter, (Type, Delegate) toString)
        {
            _filter = filter;
            _toString = toString;
        }

        public static FlowContextHandler Create(Func<Exception, bool> filter)
        {
            return new FlowContextHandler(filter, (typeof(Exception), (Exception e) => e.Message));
        }

        public static FlowContextHandler Create<TEx>(Func<TEx, string> toString) where TEx : Exception
        {
            return new FlowContextHandler(ex => ex is TEx, (typeof(TEx), toString));
        }

        private (string, bool) ToErrorStringImpl(Exception ex)
        {
            return _toString.Item1.IsInstanceOfType(ex) ? ((string)_toString.Item2.DynamicInvoke(ex)!, true) : (ex.Message, false);
        }

        private bool ShouldCatchImpl(Exception ex)
        {
            return _filter(ex);
        }
    }
}