using System.Security.Cryptography.X509Certificates;
using DotNext;

namespace auvdisk.Extensions
{
    public class Value<TSubj>
        where TSubj : struct
    {
        private Value()
        {
        }

        public Value(TSubj value)
        {
            Val = value;
        }

        public static Value<TSubj> Some(TSubj value)
        {
            return new Value<TSubj>(value);
        }

        public TSubj Val { get; private set; }
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
        public static Flow<TSubj> Optional<TSubj>(Optional<TSubj> value, string error, Log.ILog logger) where TSubj: class
        {
            return value.HasValue ? Ok(value.Value, logger) : Err<TSubj>(error, logger);
        }
        public static Flow<TSubj> Ok<TSubj>(TSubj value, Log.ILog logger) where TSubj: class
        {
            return Flow<TSubj>.Ok(value, logger);
        }
        
        public static Flow<TSubj> Err<TSubj>(string error, Log.ILog logger) where TSubj: class
        {
            return Flow<TSubj>.Err(error, logger);
        }
    }

    public static class Extensions
    {
        public static Flow<TSubj> Flow<TSubj>(this Optional<TSubj> subj, string error, Log.ILog logger) where TSubj: class
        {
            return subj.HasValue ? Flows.Ok(subj.Value, logger) : Flows.Err<TSubj>(error, logger);
        }
        
        // Defined as an extension method to be able to put constraints on TSubj : IDisposable
        public static Flow<TRes> MapDispose<TSubj, TRes>(this Flow<TSubj> subj, Func<TSubj, TRes> mapper)
            where TRes : class
            where TSubj : class, IDisposable
        {
            return subj.MapDispose(mapper, _ => subj.Unwrap());
        }

        public static Flow<TRes> MapDispose<TSubj, TDisposable, TRes>(this Flow<TSubj> subj, Func<TSubj, TRes> mapper, Func<TSubj, TDisposable> disposable)
            where TRes : class
            where TSubj : class
            where TDisposable : IDisposable
        {
            if (subj.HasValue())
            {
                var val = mapper(subj.Unwrap());
                disposable(subj.Unwrap()).Dispose();
                
                return Flows.Ok(val, subj.Logger);
            }
            else
            {
                return new(subj.UnwrapErr(), subj.Logger);
            }
        }
        
        public static Flow<Tuple<TSubj, TRes>> Concat<TSubj, TRes>(this Flow<TSubj> subj, Func<TSubj, Flow<TRes>> binder)
            where TRes : class
            where TSubj : class
        {
            if (subj.HasValue())
            {
                var otherValue = binder(subj.Unwrap());

                if (otherValue.HasValue())
                {
                    return Flows.Ok(Tuple.Create(subj.Unwrap(), otherValue.Unwrap()), subj.Logger);
                }
                else
                {
                    return Flows.Err<Tuple<TSubj, TRes>>(otherValue.UnwrapErr(), subj.Logger);
                }
            }
            else
            {
                return Flows.Err<Tuple<TSubj, TRes>>(subj.UnwrapErr(), subj.Logger);
            }
        }

        public static Flow<Tuple<T1, T2, TNext>> Concat<T1, T2, TNext>(this Flow<Tuple<T1, T2>> subj,
            Func<Tuple<T1, T2>, Flow<TNext>> binder)
                where TNext : class
        {
            if (subj.HasValue())
            {
                var otherValue = binder(subj.Unwrap());

                if (otherValue.HasValue())
                {
                    var currentValue = subj.Unwrap();

                    return Flows.Ok(Tuple.Create(currentValue.Item1, currentValue.Item2, otherValue.Unwrap()), subj.Logger);
                }
                else
                {
                    return Flows.Err<Tuple<T1, T2, TNext>>(otherValue.UnwrapErr(), subj.Logger);
                }
            }
            else
            {
                return Flows.Err<Tuple<T1, T2, TNext>>(subj.UnwrapErr(), subj.Logger);
            }
        }
        
        public static Flow<Tuple<T1, T2, T3, TNext>> Concat<T1, T2, T3, TNext>(this Flow<Tuple<T1, T2, T3>> subj,
            Func<Tuple<T1, T2, T3>, Flow<TNext>> binder)
            where TNext : class
        {
            if (subj.HasValue())
            {
                var otherValue = binder(subj.Unwrap());

                if (otherValue.HasValue())
                {
                    var currentValue = subj.Unwrap();

                    return Flows.Ok(Tuple.Create(currentValue.Item1, currentValue.Item2, currentValue.Item3, otherValue.Unwrap()), subj.Logger);
                }
                else
                {
                    return Flows.Err<Tuple<T1, T2, T3, TNext>>(otherValue.UnwrapErr(), subj.Logger);
                }
            }
            else
            {
                return Flows.Err<Tuple<T1, T2, T3, TNext>>(subj.UnwrapErr(), subj.Logger);
            }
        }
        
        public static Flow<TRes> BindConcat<TSubj, TNew, TRes>(this Flow<TSubj> subj, Func<TSubj, Flow<TNew>> binder, Func<TSubj, TNew, TRes> converter)
            where TRes : class
            where TNew: class
            where TSubj: class
        {
            if (subj.HasValue())
            {
                var newVal = binder(subj.Unwrap());

                if (newVal.HasValue())
                {
                    return Flows.Ok(converter(subj.Unwrap(), newVal.Unwrap()), subj.Logger);
                }
                else
                {
                    return new(newVal.UnwrapErr(),  subj.Logger);
                }
            }
            else
            {
                return new (subj.UnwrapErr(), subj.Logger);
            }
        }
        
        public static Flow<TRes> MapConcat<TSubj, TNew, TRes>(this Flow<TSubj> subj, Func<TSubj, TNew> transformer, Func<TSubj, TNew, TRes> converter)
            where TRes : class
            where TNew: class
            where TSubj: class
        {
            if (subj.HasValue())
            {
                var newVal = transformer(subj.Unwrap());

                return Flows.Ok(converter(subj.Unwrap(), newVal), subj.Logger);
            }
            else
            {
                return new (subj.UnwrapErr(), subj.Logger);
            }
        }
    }
    
    public class Flow<TSubj> : IDisposable
        where TSubj : class
    {
        private TSubj? Value { get; }
        private string? Error { get; }
        public Log.ILog Logger { get; }

        public Flow(string error, Log.ILog logger)
        {
            Value = null;
            Error = error;
            Logger = logger;
        }

        private Flow(Log.ILog logger, TSubj? value, string? error)
        {
            Value = value;
            Error = error;
            Logger = logger;
        }

        private Flow(Log.ILog logger, string error)
        {
            Error = error;
            Logger = logger;
        }

        public static Flow<TSubj> Ok(TSubj value, Log.ILog logger)
        {
            return new Flow<TSubj>(logger, value, null);
        }
        
        public Flow<TSubj> Finally(Action action)
        {
            action();
            return this;
        }
        
        public static Flow<TSubj> Err(string error, Log.ILog logger)
        {
            return new Flow<TSubj>(logger, error);
        }

        public Flow<TRes> MapOr<TRes>(Func<TSubj, TRes?> mapper, string error)
            where TRes : class
        {
            if (HasValue() && mapper(Value!) is { } newValue)
            {
                return new Flow<TRes>(Logger, newValue, null);
            }
            else
            {
                return new Flow<TRes>(Logger, Error ?? error);
            }
        }

        public Flow<TRes> Map<TRes>(Func<TSubj, TRes> mapper)
            where TRes : class
        {
            return HasValue()
                ? new Flow<TRes>(Logger, mapper(Value!), null)
                : new Flow<TRes>(Logger, Error ?? "Unexpected error");
        }
        
        public Flow<TSubj> CheckDiscardIf<TOther>(Func<TSubj, bool> condition, Func<TSubj, Flow<TOther>> binder) where TOther : class
        {
            if (HasValue() && condition(Value!))
            {
                var result = binder(Value!);

                return result.HasValue() ? this : Err(result.Error!, Logger);
            }

            return this;
        }
        
        public Flow<TSubj> CheckDiscard<TOther>(Func<TSubj, Flow<TOther>> binder) where TOther : class
        {
            if (HasValue())
            {
                var result = binder(Value!);

                return result.HasValue() ? this : Err(result.Error!, Logger);
            }

            return this;
        }
        
        public Flow<TSubj> WithSideEffect(Action<TSubj> action)
        {
            if (HasValue())
            {
                action(Value!);
            }

            return this;
        }

        public Flow<TSubj> WithSideEffect(Action action)
        {
            if (HasValue())
            {
                action();
            }

            return this;
        }
        
        public Flow<TRes> Bind<TRes>(Func<TSubj, Flow<TRes>> binder)
            where TRes : class
        {
            if (HasValue())
            {
                return binder(Value!);
            }
            else
            {
                return new Flow<TRes>(Logger, Error ?? "Unexpected error");
            }
        }

        public Flow<TRes> TryMap<TRes, TE1>(Func<TSubj, TRes> mapper)
            where TRes : class
            where TE1 : Exception
        {
            try
            {
                return HasValue()
                    ? new Flow<TRes>(Logger, mapper(Value!), null)
                    : new Flow<TRes>(Logger, Error ?? "Unexpected error");
            }
            catch (TE1 ex)
            {
                return new Flow<TRes>(Logger, ex.Message);
            }
        }

        public Flow<TSubj> Log(string msg)
        {
            if (HasValue())
            {
                Logger.Log(msg);
            }

            return this;
        }

        public Flow<TSubj> LogIf(Func<TSubj, bool> condition, string msg)
        {
            if (HasValue() && condition(Value!))
            {
                Logger.Log(msg);
            }

            return this;
        }

        public Flow<TSubj> Check(Func<TSubj, bool> predicate, Func<TSubj, string> error)
        {
            if (HasValue())
            {
                if (predicate(Value!))
                {
                    return this;
                }
                else
                {
                    return new Flow<TSubj>(Logger, Error ?? error(Value!));
                }
            }
            else
            {
                return this;
            }
        }

        public TSubj Unwrap()
        {
            if (HasValue())
            {
                return Value!;
            }
            else
            {
                throw new NullReferenceException();
            }
        }

        public string UnwrapErr()
        {
            if (Error != null)
            {
                return Error;
            }
            else
            {
                throw new NullReferenceException();
            }
        }

        public bool LogErrorIfAny()
        {
            if (Error != null)
            {
                Logger.Error(Error);

                return true;
            }

            return false;
        }

        public void Dispose()
        {
            if (Value is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        public bool IsError()
        {
            return Error != null;
        }

        public bool HasValue()
        {
            return Value != null;
        }
    }
}