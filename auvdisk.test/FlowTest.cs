using auvdisk.Extensions;
using auvdisk.Log;

namespace auvdisk.test
{
    public class FlowTest
    {
        public FlowTest()
        {
            Program.IsInteractive = false;
        }
        
        private LogWatcher Logger { get; } = new LogWatcher();

        [Fact]
        public void TestMap()
        {
            var res = Flow<Value<int>>.Val(2.RefVal()).Map(x => ((double)x.Val).RefVal());
            Assert.True(res.IsVal);

            bool sideEffectWasExecuted = false;
            res = res.WithSideEffect(() => sideEffectWasExecuted = true);
            Assert.True(sideEffectWasExecuted);

            res = Flow<Value<int>>.Err("Divide by zero").Map<Value<int>, Value<double>>(x => throw new InvalidOperationException());
            Assert.False(res.IsVal);
            Assert.Equal("Divide by zero", res.UnwrapErr());

            res.WithSideEffect(() => throw new InvalidOperationException());
        }

        [Fact]
        public void TestMapOr()
        {
            var res = Flow<Value<int>>.Val(3.RefVal()).MapOr(x => x.Val % 2 == 0 ? ((double)x.Val).RefVal() : null, "Number is odd");
            Assert.Throws<NullReferenceException>(() => res.UnwrapVal());
            Assert.Equal("Number is odd", res.UnwrapErr());

            res = res.MapOr(x => x, "Another error");
            Assert.Equal("Number is odd", res.UnwrapErr());

        }

        [Fact]
        public void TestMapDispose()
        {
            var disposable = new DisposableDummy();
            bool sideEffectWasExecuted = false;

            var result = Flow<DisposableDummy>.Val(disposable).MapDispose(x => x.DummyMember.RefVal());
            Assert.True(result.IsVal);
            Assert.Equal(42, result.UnwrapVal().Val);
            Assert.True(disposable.Disposed);

            result = Flow<Value<int>>.Val(42.RefVal()).Map(x => (x.Val * 2).RefVal());
            Assert.True(result.IsVal);
            Assert.Equal(42 * 2, result.UnwrapVal().Val);
            Assert.Throws<NullReferenceException>(() => result.UnwrapErr());
            Assert.False(result.IsErr);
            result.LogErrorIfAny(Logger);
            Assert.Empty(Logger.GetError());
            result.WithSideEffect(() => sideEffectWasExecuted = true);
            Assert.True(sideEffectWasExecuted);
            result = Flow<Value<int>>.Err("Divide by zero").Map<Value<int>, Value<int>>(x => throw new InvalidOperationException());
            Assert.False(result.IsVal);
            Assert.Throws<NullReferenceException>(() => result.UnwrapVal());
            Assert.Equal("Divide by zero", result.UnwrapErr());
            Assert.True(result.IsErr);
            result.WithSideEffect(() => sideEffectWasExecuted = false);
            Assert.True(sideEffectWasExecuted);
            result.LogErrorIfAny(Logger);
            Assert.Single(Logger.GetError());
            Assert.Equal("Divide by zero", Logger.GetError().First());
        }

        [Fact]
        public void TestTryMap()
        {
            Func<Value<int>, string> MakeMapper<TEx>() where TEx : Exception, new()
            {
                return _ => throw new TEx();
            }

            var result = Flow<Value<int>>.Val(42.RefVal())
                .TryMap(MakeMapper<InvalidOperationException>(), (InvalidOperationException e) => e.Message);

            Assert.False(result.IsVal);
            Assert.Throws<DivideByZeroException>(() =>
                Flow<Value<int>>.Val(42.RefVal())
                    .TryMap(MakeMapper<DivideByZeroException>(), (InvalidOperationException e) => e.Message));

            result = Flow<Value<int>>.Err("Divide by zero")
                .TryMap(MakeMapper<NullReferenceException>(), (InvalidOperationException e) => e.Message);

            Assert.False(result.IsVal);
        }

        [Fact]
        public void TestBind()
        {
            var result = Flows.RefVal(42).Bind((x) => Flows.Val(x.Val.ToString()));

            Assert.True(result.IsVal);
            Assert.Equal("42", result.UnwrapVal());

            result = Flows.RefVal(42).Bind((x) => Flow<string>.Err("Divide by zero"));
            Assert.False(result.IsVal);
            Assert.Throws<NullReferenceException>(() => result.UnwrapVal());

            result = result.Bind<string, string>((x) => throw new NullReferenceException());
            Assert.Equal("Divide by zero", result.UnwrapErr());
        }
    }
}