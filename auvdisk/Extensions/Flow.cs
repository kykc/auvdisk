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
        public static Flow<TSubj> Optional<TSubj>(Optional<TSubj> value, string error, IFlowContext? context = null) where TSubj: class
        {
            context ??= Flow<TSubj>.DefaultCtx();
            
            return value.HasValue ? Val(value.Value, context) : Err<TSubj>(error, context);
        }
        
        public static Flow<TSubj> Val<TSubj>(TSubj value, IFlowContext? context = null) where TSubj: class
        {
            return Flow<TSubj>.Val(value, context ?? Flow<TSubj>.DefaultCtx());
        }

        public static Flow<TSubj> ValOr<TSubj>(TSubj? value, string error, IFlowContext? context = null) where TSubj : class
        {
            context ??= Flow<TSubj>.DefaultCtx();
            
            return value != null ? Val(value, context) : Err<TSubj>(error, context);
        }
        
        public static Flow<TSubj> Err<TSubj>(string error, IFlowContext? context = null) where TSubj: class
        {
            return Flow<TSubj>.Err(error, context ?? Flow<TSubj>.DefaultCtx());
        }

        public static Flow<Value<TSubj>> RefVal<TSubj>(TSubj value, IFlowContext? context = null) where TSubj : struct
        {
            return Val(value.RefVal(), context);
        }
    }
    
    public static class FlowExtensions
    {
        public static Flow<TSubj> Flow<TSubj>(this Optional<TSubj> subj, string error, IFlowContext? context = null) where TSubj: class
        {
            return subj.HasValue ? Flows.Val(subj.Value, context) : Flows.Err<TSubj>(error, context);
        }

        public static Flow<TSubj> Flow<TSubj>(this TSubj? subj, string error, IFlowContext? context = null) where TSubj : class
        {
            return Flows.ValOr(subj, error, context);
        }
        
        public static Flow<Value<TSubj>> Flow<TSubj>(this TSubj? subj, string error, IFlowContext? context = null) where TSubj : struct
        {
            return subj.HasValue ? Flows.Val(new Value<TSubj>(subj.Value), context) : Flows.Err<Value<TSubj>>(error, context);
        }

        public static Flow<TSubj> Flow<TSubj>(this TSubj subj, IFlowContext? context = null) where TSubj : class
        {
            return Flows.Val(subj, context);
        }

        public static Flow<None> Flow(this bool value, string error, IFlowContext? context = null)
        {
            return value ? Flows.Val(None.Value, context) : Flows.Err<None>(error, context); 
        }
    }

    public static class Extensions
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
            Flow<TNew> BinderWrapper(TSubj s, IFlowContext ctx) => binder(s);

            return subj.BindConcat(BinderWrapper, converter, changeContext);
        }
        
        public static Flow<TRes> BindConcat<TSubj, TNew, TRes>(this Flow<TSubj> subj, Func<TSubj, IFlowContext, Flow<TNew>> binder, Func<TSubj, TNew, TRes> converter, bool changeContext = false)
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

        public static Flow<TSubj> GetContext<TSubj>(this Flow<TSubj> subj, Action<IFlowContext> action) where TSubj : class
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
        
        public static Flow<TRes> Bind<TSubj, TRes>(this Flow<TSubj> subj, Func<TSubj, IFlowContext, Flow<TRes>> binder, bool switchContext = false)
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
            Flow<TOther> BinderAdapter(TSubj s, IFlowContext ctx) => binder(s);

            return subj.BindErrIf(condition, BinderAdapter, switchContext);
        }
        
        public static Flow<TSubj> BindErrIf<TSubj, TOther>(this Flow<TSubj> subj, Func<TSubj, bool> condition, Func<TSubj, IFlowContext, Flow<TOther>> binder, bool switchContext = false) 
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
        
        public static Flow<TSubj> BindErr<TSubj, TOther>(this Flow<TSubj> subj, Func<TSubj, IFlowContext, Flow<TOther>> binder, bool switchContext = false) 
            where TSubj : class
            where TOther : class
        {
            return subj.BindConcat(binder, (left, _) => left, switchContext);
        }
        
        public static Flow<TSubj> Finally<TSubj>(this Flow<TSubj> subj, Action<IFlowContext> action) where TSubj : class
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
        private readonly IFlowContext _context;
        public bool IsErr => _error != null;
        public bool IsVal => _value != null;
        public IFlowContext Context => _context;
        
        public static Func<IFlowContext> DefaultCtx => () => new DefaultFlowContext();

        // This allows to construct error Flow w/o naming its type explicitly
        public Flow(string error, IFlowContext? context = null)
        {
            _value = null;
            _error = error;
            _context = context ?? DefaultCtx();
        }

        private Flow(TSubj? value, string? error, IFlowContext context)
        {
            Debug.Assert(value != null || error != null, "value != null || error != null");
            
            _value = value;
            _error = error;
            _context = context;
        }

        public static Flow<TSubj> Val(TSubj value, IFlowContext context)
        {
            if (value is IDisposable disposable)
            {
                context.AddDisposable(disposable);
            }
            
            return new Flow<TSubj>(value, null, context);
        }
        
        public static Flow<TSubj> Err(string error, IFlowContext context)
        {
            return new Flow<TSubj>(null, error, context);
        }

        public Flow<TSubj> WithCtx(IFlowContext context)
        {
            return new(_value, _error, _context.With(context));
        }

        public Flow<TSubj> Handle<TEx>(Func<TEx, string> handleString) where TEx : Exception
        {
            return WithCtx(new DefaultFlowContext().Handle(handleString));
        }

        public Flow<TSubj> PopCtx()
        {
            return new(_value, _error, _context.Pop());
        }

        public Flow<TSubj> HandleAll()
        {
            return WithCtx(new DefaultFlowContext().Handle((Exception ex) => ex.Message));
        }
        
        public Flow<TSubj> ResetCtx()
        {
            return new(_value, _error, DefaultCtx());
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
            foreach (var disposable in _context.GetDisposables().Distinct().ToList())
            {
                try
                {
                    disposable.Dispose();
                    _context.RemoveDisposable(disposable);
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

    public interface IFlowContext
    {
        Func<Exception, bool> ShouldCatch { get; }
        Func<Exception, (string, bool)> ToErrorString { get; }

        IFlowContext With(IFlowContext context);
        IFlowContext Pop();
        
        void AddDisposable(IDisposable disposable);
        void RemoveDisposable(IDisposable disposable);
        IEnumerable<IDisposable> GetDisposables();
    }

    class DefaultFlowContext : IFlowContext
    {
        private readonly List<(Type, Delegate)> _handlersString = [];
        private readonly List<Func<Exception, bool>> _handlersFilter = [];
        private readonly LinkedList<IFlowContext> _others = [];
        private readonly HashSet<IDisposable> _disposables = [];

        public Func<Exception, bool> ShouldCatch => ShouldCatchImpl;
        public Func<Exception, (string, bool)> ToErrorString => ToErrorStringImpl;
        
        public bool IsEmpty => _handlersString.Count == 0 && _handlersFilter.Count == 0 && _others.Count == 0 && _disposables.Count == 0;
        
        // For UTs
        internal IEnumerable<IFlowContext> Others => _others;
        
        public IFlowContext With(IFlowContext context)
        {
            _others.AddFirst(context);

            return this;
        }

        public IFlowContext Pop()
        {
            var target = this;
            Debug.Assert(target._others.Count > 0, "_others.Count != 0");
            if (target._others.Count <= 0) return target;
            
            target._others.First().GetDisposables().ForEach(d => target.AddDisposable(d));
            target._others.RemoveFirst();

            return target;
        }

        public void AddDisposable(IDisposable disposable)
        {
            _disposables.Add(disposable);
        }

        public void RemoveDisposable(IDisposable disposable)
        {
            _disposables.Remove(disposable);
            _others.ForEach(o => o.RemoveDisposable(disposable));
        }

        public IEnumerable<IDisposable> GetDisposables()
        {
            return _disposables.Union(_others.SelectMany(o => o.GetDisposables()));
        }

        private void AddStringHandler<TEx>(Func<TEx, string> handler) where TEx : Exception
        {
            _handlersString.Add((typeof(TEx), handler));
        }

        private void AddFilterHandler(Func<Exception, bool> filter)
        {
            _handlersFilter.Add(filter);
        }

        public DefaultFlowContext Handle<TEx>(Func<TEx, string> handleString, Func<Exception, bool> handleFilter) where TEx : Exception
        {
            var newInst = this;
            
            newInst.AddStringHandler(handleString);
            newInst.AddFilterHandler(handleFilter);
            
            return newInst;
        }

        public DefaultFlowContext Handle<TEx>(Func<TEx, string> handleString) where TEx : Exception
        {
            var newInst = this;
            
            newInst.Handle(handleString, e => e is TEx);

            return newInst;
        }

        private (string, bool) ToErrorStringImpl(Exception ex)
        {
            foreach (var other in _others)
            {
                if (other.ToErrorString(ex) is (var str, true))
                {
                    return (str, true);
                }
            }
            
            foreach (var item in _handlersString.Where(x => x.Item1.IsInstanceOfType(ex)))
            {
                return ((string)item.Item2.DynamicInvoke(ex)!, true);
            }
            
            return (ex.Message, false);
        }

        private bool ShouldCatchImpl(Exception ex)
        {
            return _others.Any(x => x.ShouldCatch(ex)) || _handlersFilter.Any(x => x(ex));
        }
    }
}