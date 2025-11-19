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
        public static Flow<TSubj> Optional<TSubj>(Optional<TSubj> value, string error) where TSubj: class
        {
            return value.HasValue ? Val(value.Value) : Err<TSubj>(error);
        }
        
        public static Flow<TSubj> Val<TSubj>(TSubj value) where TSubj: class
        {
            return Flow<TSubj>.Val(value);
        }

        public static Flow<TSubj> ValOr<TSubj>(TSubj? value, string error) where TSubj : class
        {
            return value != null ? Val(value) : Err<TSubj>(error);
        }
        
        public static Flow<TSubj> Err<TSubj>(string error) where TSubj: class
        {
            return Flow<TSubj>.Err(error);
        }

        public static Flow<Value<TSubj>> RefVal<TSubj>(TSubj value) where TSubj : struct
        {
            return Val(value.RefVal());
        }
    }

    public static class Extensions
    {
        public static Flow<TSubj> Flow<TSubj>(this Optional<TSubj> subj, string error) where TSubj: class
        {
            return subj.HasValue ? Flows.Val(subj.Value) : Flows.Err<TSubj>(error);
        }

        public static Flow<TSubj> Flow<TSubj>(this TSubj? subj, string error) where TSubj : class
        {
            return Flows.ValOr(subj, error);
        }
        
        public static Flow<Value<TSubj>> Flow<TSubj>(this TSubj? subj, string error) where TSubj : struct
        {
            return subj.HasValue ? Flows.Val(new Value<TSubj>(subj.Value)) : Flows.Err<Value<TSubj>>(error);
        }

        public static Flow<None> Flow(this bool value, string error)
        {
            return value ? Flows.Val(None.Value) : Flows.Err<None>(error); 
        }
        
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
            if (subj.IsVal)
            {
                var val = mapper(subj.UnwrapVal());
                disposable(subj.UnwrapVal()).Dispose();
                
                return Flows.Val(val);
            }
            else
            {
                return new(subj.UnwrapErr());
            }
        }
        
        public static Flow<Tuple<TSubj, TRes>> Concat<TSubj, TRes>(this Flow<TSubj> subj, Func<TSubj, Flow<TRes>> binder)
            where TRes : class
            where TSubj : class
        {
            if (subj.IsVal)
            {
                var otherValue = binder(subj.UnwrapVal());

                if (otherValue.IsVal)
                {
                    return Flows.Val(Tuple.Create(subj.UnwrapVal(), otherValue.UnwrapVal()));
                }
                else
                {
                    return Flows.Err<Tuple<TSubj, TRes>>(otherValue.UnwrapErr());
                }
            }
            else
            {
                return Flows.Err<Tuple<TSubj, TRes>>(subj.UnwrapErr());
            }
        }

        public static Flow<Tuple<T1, T2, TNext>> Concat<T1, T2, TNext>(this Flow<Tuple<T1, T2>> subj,
            Func<Tuple<T1, T2>, Flow<TNext>> binder)
                where TNext : class
        {
            if (subj.IsVal)
            {
                var otherValue = binder(subj.UnwrapVal());

                if (otherValue.IsVal)
                {
                    var currentValue = subj.UnwrapVal();

                    return Flows.Val(Tuple.Create(currentValue.Item1, currentValue.Item2, otherValue.UnwrapVal()));
                }
                else
                {
                    return Flows.Err<Tuple<T1, T2, TNext>>(otherValue.UnwrapErr());
                }
            }
            else
            {
                return Flows.Err<Tuple<T1, T2, TNext>>(subj.UnwrapErr());
            }
        }
        
        public static Flow<Tuple<T1, T2, T3, TNext>> Concat<T1, T2, T3, TNext>(this Flow<Tuple<T1, T2, T3>> subj,
            Func<Tuple<T1, T2, T3>, Flow<TNext>> binder)
            where TNext : class
        {
            if (subj.IsVal)
            {
                var otherValue = binder(subj.UnwrapVal());

                if (otherValue.IsVal)
                {
                    var currentValue = subj.UnwrapVal();

                    return Flows.Val(Tuple.Create(currentValue.Item1, currentValue.Item2, currentValue.Item3, otherValue.UnwrapVal()));
                }
                else
                {
                    return Flows.Err<Tuple<T1, T2, T3, TNext>>(otherValue.UnwrapErr());
                }
            }
            else
            {
                return Flows.Err<Tuple<T1, T2, T3, TNext>>(subj.UnwrapErr());
            }
        }
        
        public static Flow<TRes> BindConcat<TSubj, TNew, TRes>(this Flow<TSubj> subj, Func<TSubj, Flow<TNew>> binder, Func<TSubj, TNew, TRes> converter)
            where TRes : class
            where TNew: class
            where TSubj: class
        {
            if (subj.IsVal)
            {
                var newVal = binder(subj.UnwrapVal());

                if (newVal.IsVal)
                {
                    return Flows.Val(converter(subj.UnwrapVal(), newVal.UnwrapVal()));
                }
                else
                {
                    return new(newVal.UnwrapErr());
                }
            }
            else
            {
                return new (subj.UnwrapErr());
            }
        }
        
        public static Flow<TRes> MapConcat<TSubj, TNew, TRes>(this Flow<TSubj> subj, Func<TSubj, TNew> transformer, Func<TSubj, TNew, TRes> converter)
            where TRes : class
            where TNew: class
            where TSubj: class
        {
            if (subj.IsVal)
            {
                var newVal = transformer(subj.UnwrapVal());

                return Flows.Val(converter(subj.UnwrapVal(), newVal));
            }
            else
            {
                return new (subj.UnwrapErr());
            }
        }
        
        public static Flow<TSubj> WithSideEffect<TSubj>(this Flow<TSubj> subj, Action<TSubj> action) where TSubj : class
        {
            if (subj.IsVal)
            {
                action(subj.UnwrapVal());
            }

            return subj;
        }

        public static Flow<TSubj> WithSideEffect<TSubj>(this Flow<TSubj> subj, Action action) where TSubj : class
        {
            if (subj.IsVal)
            {
                action();
            }

            return subj;
        }
        
        public static Flow<TRes> Bind<TSubj, TRes>(this Flow<TSubj> subj, Func<TSubj, Flow<TRes>> binder)
            where TSubj : class
            where TRes : class
        {
            if (subj.IsVal)
            {
                return binder(subj.UnwrapVal());
            }
            else
            {
                return new(subj.UnwrapErr());
            }
        }

        public static Flow<TRes> TryMap<TSubj, TRes, TE1>(this Flow<TSubj> subj, Func<TSubj, TRes> mapper, Func<TE1, string> exToString)
            where TSubj : class
            where TRes : class
            where TE1 : Exception
        {
            try
            {
                return subj.IsVal
                    ? Flows.Val(mapper(subj.UnwrapVal()))
                    : new (subj.UnwrapErr());
            }
            catch (TE1 ex)
            {
                return new(exToString(ex));
            }
        }

        public static Flow<TSubj> LogOk<TSubj>(this Flow<TSubj> subj, ILog logger, string msg) where TSubj : class
        {
            if (subj.IsVal)
            {
                logger.Log(msg);
            }

            return subj;
        }
        
        public static Flow<TSubj> LogOk<TSubj>(this Flow<TSubj> subj, ILog logger, Func<TSubj, string> msg) where TSubj : class
        {
            if (subj.IsVal)
            {
                logger.Log(msg(subj.UnwrapVal()));
            }

            return subj;
        }

        public static Flow<TSubj> LogIf<TSubj>(this Flow<TSubj> subj, ILog logger, Func<TSubj, bool> condition, string msg) where TSubj : class
        {
            if (subj.IsVal && condition(subj.UnwrapVal()))
            {
                logger.Log(msg);
            }

            return subj;
        }

        public static Flow<TSubj> Check<TSubj>(this Flow<TSubj> subj, Func<TSubj, bool> predicate, Func<TSubj, string> error) where TSubj : class
        {
            if (subj.IsVal)
            {
                return predicate(subj.UnwrapVal()) ? subj : new(error(subj.UnwrapVal()));
            }
            else
            {
                return subj;
            }
        }
        
        public static Flow<TRes> MapOr<TSubj, TRes>(this Flow<TSubj> subj, Func<TSubj, TRes?> mapper, string error)
            where TSubj : class
            where TRes : class
        {
            if (subj.IsVal && mapper(subj.UnwrapVal()) is { } newValue)
            {
                return Flows.Val(newValue);
            }
            else
            {
                return new (subj.IsErr ? subj.UnwrapErr() : error);
            }
        }

        public static Flow<TRes> Map<TSubj, TRes>(this Flow<TSubj> subj, Func<TSubj, TRes> mapper)
            where TSubj : class
            where TRes : class
        {
            return subj.IsVal
                ? Flows.Val(mapper(subj.UnwrapVal()))
                : new (subj.UnwrapErr());
        }
        
        public static Flow<TSubj> CheckDiscardIf<TSubj, TOther>(this Flow<TSubj> subj, Func<TSubj, bool> condition, Func<TSubj, Flow<TOther>> binder) 
            where TSubj : class
            where TOther : class
        {
            if (!subj.IsVal || !condition(subj.UnwrapVal())) return subj;
            
            var result = binder(subj.UnwrapVal());

            return result.IsVal ? subj : new (result.UnwrapErr());
        }
        
        // AKA BindErr
        public static Flow<TSubj> CheckDiscard<TSubj, TOther>(this Flow<TSubj> subj, Func<TSubj, Flow<TOther>> binder) 
            where TSubj : class
            where TOther : class
        {
            if (!subj.IsVal) return subj;
            
            var result = binder(subj.UnwrapVal());

            return result.IsVal ? subj : new(result.UnwrapErr());
        }
        
        public static Flow<TSubj> Finally<TSubj>(this Flow<TSubj> subj, Action action) where TSubj : class
        {
            action();
            return subj;
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

        // This allows to construct error Flow w/o naming its type explicitly
        public Flow(string error)
        {
            _value = null;
            _error = error;
        }

        private Flow(TSubj? value, string? error)
        {
            _value = value;
            _error = error;
        }

        public static Flow<TSubj> Val(TSubj value)
        {
            return new Flow<TSubj>(value, null);
        }
        
        public static Flow<TSubj> Err(string error)
        {
            return new Flow<TSubj>(null, error);
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
            if (_value is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }
}