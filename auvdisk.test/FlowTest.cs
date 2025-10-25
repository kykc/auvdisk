using auvdisk.Extensions;
using auvdisk.Log;

namespace auvdisk.test
{
    public class FlowTest
    {
        private ILog Logger { get; } = new LogWatcher();

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

            var result = Flow<DisposableDummy>.Ok(disposable, Logger).MapDispose(x => x.DummyMember.Some());
            Assert.True(result.HasValue());
            Assert.Equal(42, result.Unwrap().Val);
            Assert.True(disposable.Disposed);
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
    }
}