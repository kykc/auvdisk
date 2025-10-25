using auvdisk.Extensions;
using auvdisk.Log;

namespace auvdisk.test
{
    public class FlowTest
    {
        private LogWatcher Logger { get; } = new LogWatcher();

        [Fact]
        public void TestMap()
        {
            var res = Flow<Value<int>>.Ok(2.Some(), Logger).Map(x => ((double)x.Val).Some());
            Assert.True(res.HasValue());

            bool sideEffectWasExecuted = false;
            res = res.WithSideEffect(() => sideEffectWasExecuted = true);
            Assert.True(sideEffectWasExecuted);

            res = Flow<Value<int>>.Err("Divide by zero", Logger).Map<Value<double>>(x => throw new InvalidOperationException());
            Assert.False(res.HasValue());
            Assert.Equal("Divide by zero", res.UnwrapErr());

            res.WithSideEffect(() => throw new InvalidOperationException());
        }

        [Fact]
        public void TestMapOr()
        {
            var res = Flow<Value<int>>.Ok(3.Some(), Logger).MapOr(x => x.Val % 2 == 0 ? ((double)x.Val).Some() : null, "Number is odd");
            Assert.Throws<NullReferenceException>(() => res.Unwrap());
            Assert.Equal("Number is odd", res.UnwrapErr());

            res = res.MapOr(x => x, "Another error");
            Assert.Equal("Number is odd", res.UnwrapErr());

        }

        [Fact]
        public void TestMapDispose()
        {
            var disposable = new DisposableDummy();
            bool sideEffectWasExecuted = false;

            var result = Flow<DisposableDummy>.Ok(disposable, Logger).MapDispose(x => x.DummyMember.Some());
            Assert.True(result.HasValue());
            Assert.Equal(42, result.Unwrap().Val);
            Assert.True(disposable.Disposed);

            result = Flow<Value<int>>.Ok(42.Some(), Logger).MapDispose(x => (x.Val * 2).Some());
            Assert.True(result.HasValue());
            Assert.Equal(42 * 2, result.Unwrap().Val);
            Assert.Throws<NullReferenceException>(() => result.UnwrapErr());
            Assert.False(result.IsError());
            result.WithSideEffect(() => sideEffectWasExecuted = true, () => false);
            Assert.False(sideEffectWasExecuted);
            result.WithSideEffect(() => sideEffectWasExecuted = true, () => true);
            Assert.True(sideEffectWasExecuted);
            result.LogErrorIfAny();
            Assert.Empty(Logger.GetError());

            result = Flow<Value<int>>.Err("Divide by zero", Logger).MapDispose<Value<int>>(x => throw new InvalidOperationException());
            Assert.False(result.HasValue());
            Assert.Throws<NullReferenceException>(() => result.Unwrap());
            Assert.Equal("Divide by zero", result.UnwrapErr());
            Assert.True(result.IsError());
            result.WithSideEffect(() => sideEffectWasExecuted = false);
            Assert.True(sideEffectWasExecuted);
            result.LogErrorIfAny();
            Assert.Single(Logger.GetError());
            Assert.Equal("Divide by zero", Logger.GetError().First());
        }

        [Fact]
        public void TestMapDisposeOr()
        {
            var disposable = new DisposableDummy();

            var result = Flow<DisposableDummy>.Ok(disposable, Logger).MapDisposeOr(x => x.DummyMember.Some(), "");
            Assert.True(result.HasValue());
            Assert.Equal(42, result.Unwrap().Val);
            Assert.True(disposable.Disposed);
        }

        [Fact]
        public void TestTryMap()
        {
            var result = Flow<Value<int>>.Ok(42.Some(), Logger)
                .TryMap<string, InvalidOperationException>((_) => throw new InvalidOperationException());

            Assert.False(result.HasValue());
            Assert.Throws<DivideByZeroException>(() =>
                Flow<Value<int>>.Ok(42.Some(), Logger)
                    .TryMap<string, InvalidOperationException>((_) => throw new DivideByZeroException()));

            result = Flow<string>.Err("Divide by zero", Logger)
                .TryMap<string, NullReferenceException>(_ => throw new InvalidOperationException());

            Assert.False(result.HasValue());
        }

        [Fact]
        public void TestBind()
        {
            var result = Flow<Value<int>>.Ok(42.Some(), Logger).Bind((x) => Flow<string>.Ok(x.Val.ToString(), Logger));

            Assert.True(result.HasValue());
            Assert.Equal("42", result.Unwrap());

            result = Flow<Value<int>>.Ok(42.Some(), Logger).Bind((x) => Flow<string>.Err("Divide by zero", Logger));
            Assert.False(result.HasValue());
            Assert.Throws<NullReferenceException>(() => result.Unwrap());

            result = result.Bind<string>((x) => throw new NullReferenceException());
            Assert.Equal("Divide by zero", result.UnwrapErr());
        }
    }
}