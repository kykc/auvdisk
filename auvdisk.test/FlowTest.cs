using System.ComponentModel;
using auvdisk.Extensions;

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
            var res = Flows.Val(2.RefVal()).Map(x => ((double)x.Val).RefVal());
            Assert.True(res.IsVal);

            bool sideEffectWasExecuted = false;
            res.SideEffect(_ => sideEffectWasExecuted = true);
            Assert.True(sideEffectWasExecuted);

            res = Flows.Err<Value<int>>("Divide by zero").Map<Value<int>, Value<double>>(_ => throw new InvalidOperationException());
            Assert.False(res.IsVal);
            Assert.Equal("Divide by zero", res.UnwrapErr());

            res.SideEffect(_ => throw new InvalidOperationException());
        }

        [Fact]
        public void TestMapOr()
        {
            var res = Flows.Val(3.RefVal()).MapOr(x => x.Val % 2 == 0 ? ((double)x.Val).RefVal() : null, "Number is odd");
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

            var result = Flows.Val(disposable).MapDispose(x => x.DummyMember.RefVal());
            Assert.True(result.IsVal);
            Assert.Equal(42, result.UnwrapVal().Val);
            Assert.True(disposable.Disposed);

            result = Flows.Val(42.RefVal()).Map(x => (x.Val * 2).RefVal());
            Assert.True(result.IsVal);
            Assert.Equal(42 * 2, result.UnwrapVal().Val);
            Assert.Throws<NullReferenceException>(() => result.UnwrapErr());
            Assert.False(result.IsErr);
            result.LogErrorIfAny(Logger);
            Assert.Empty(Logger.GetError());
            result.SideEffect(_ => sideEffectWasExecuted = true);
            Assert.True(sideEffectWasExecuted);
            result = Flows.Err<Value<int>>("Divide by zero").Map<Value<int>, Value<int>>(_ => throw new InvalidOperationException());
            Assert.False(result.IsVal);
            Assert.Throws<NullReferenceException>(() => result.UnwrapVal());
            Assert.Equal("Divide by zero", result.UnwrapErr());
            Assert.True(result.IsErr);
            result.SideEffect(_ => sideEffectWasExecuted = false);
            Assert.True(sideEffectWasExecuted);
            result.LogErrorIfAny(Logger);
            Assert.Single(Logger.GetError());
            Assert.Equal("Divide by zero", Logger.GetError().First());
        }

        [Fact]
        public void TestContextDisposables()
        {
            var result = Flows.Val(new DisposableDummy())
                .MapConcat(_ => 42.RefVal(), (left, right) => new { left, right });

            Assert.Single(result.Context.GetDisposables());
            
            result.Dispose();
            Assert.Empty(result.Context.GetDisposables());
            result.Dispose();
            
            var result2 = Flows.Val(None.Value)
                .Map(_ => new DisposableDummy())
                .GetContext((ctx) => Assert.Single(ctx.GetDisposables()))
                .MapConcat(_ => 42.RefVal(), (left, right) => new { left, right })
                .MapDispose(state => state.right, state => state.left);
            
            Assert.Empty(result2.Context.GetDisposables());
            result2.Dispose();
        }
        
        [Fact]
        public void TestTryMap()
        {
            Func<Value<int>, string> MakeMapper<TEx>() where TEx : Exception, new()
            {
                return _ => throw new TEx();
            }
            
            var result = Flows.Val(42.RefVal())
                .Handle((InvalidOperationException e) => e.Message)
                .Map(MakeMapper<InvalidOperationException>())
                .PopHandler();

            Assert.False(result.IsVal);
            Assert.Throws<DivideByZeroException>(() =>
                Flows.Val(42.RefVal())
                    .Handle((InvalidOperationException e) => e.Message)
                    .Map(MakeMapper<DivideByZeroException>())
                    .PopHandler());

            result = Flows.Err<Value<int>>("Divide by zero")
                .Handle((InvalidOperationException e) => e.Message)
                .Map(MakeMapper<NullReferenceException>())
                .PopHandler();

            Assert.False(result.IsVal);
        }

        [Fact]
        public void TestBind()
        {
            var result = Flows.RefVal(42).Bind((x) => Flows.Val(x.Val.ToString()));

            Assert.True(result.IsVal);
            Assert.Equal("42", result.UnwrapVal());

            result = Flows.RefVal(42).Bind(_ => Flows.Err<string>("Divide by zero"));
            Assert.False(result.IsVal);
            Assert.Throws<NullReferenceException>(() => result.UnwrapVal());

            result = result.Bind<string, string>(_ => throw new NullReferenceException());
            Assert.Equal("Divide by zero", result.UnwrapErr());
        }

        [Fact]
        public void TestContextPop()
        {
            var result = Flows.Val(None.Value)
                .Handle((InvalidOperationException e) => e.Message)
                .SideEffect(_ => throw new InvalidOperationException());
            
            FlowContext ctx = result.Context;
            
            Assert.NotNull(ctx);
            Assert.Single(ctx.Handlers);
            Assert.True(result.IsErr);
            
            var result2 = Flows.Val(None.Value)
                .Handle((InvalidOperationException e) => e.Message)
                .Map(_ => 42.RefVal())
                .PopHandler();
            
            ctx = result2.Context;
            
            Assert.NotNull(ctx);
            Assert.Empty(ctx.Handlers);
            Assert.True(result2.IsVal);
            
            Assert.Throws<InvalidOperationException>(() => result2 = result2.SideEffect(_ => throw new InvalidOperationException()));
            Assert.True(result2.IsVal); // Unhandled exception, result2 value didn't change

            result2 = result2.Handle((InvalidOperationException e) => e.Message).SideEffect(_ => throw new InvalidOperationException());
            Assert.True(result2.IsErr); // Now it contains error, as exception was handled
            
            // This throws, as ArgumentNullException wasn't added
            Assert.Throws<ArgumentNullException>(() => Flows.Val(None.Value)
                .Handle((InvalidOperationException e) => e.Message)
                .SideEffect(_ => throw new ArgumentNullException()));
        }

        [Fact]
        public void TestContextPopWithDisposable()
        {
            var result = Flows.Val(None.Value)
                .HandleAll()
                .Map(_ => new DisposableDummy())
                .PopHandler();
            
            result.Dispose();
            Assert.True(result.UnwrapVal().Disposed);

            var set = new HashSet<int>();
            set.Add(42);
            set.Add(42);
            Assert.Single(set);
            
            var ctx2 = new FlowContext();
            ctx2.With(FlowContextHandler.Create((NullReferenceException ex) => ex.Message));
            ctx2.With(FlowContextHandler.Create((InvalidOperationException ex) => ex.Message));
            var result2 = Flow<None>.Val(None.Value, ctx2)
                .PopHandler()
                .SideEffect(_ => throw new NullReferenceException());
            
            Assert.True(result2.IsErr);
            Assert.Single(result2.Context.Handlers);
        }

        [Fact]
        public void TestContext()
        {
            IFlowContextHandler contextHandler = FlowContextHandler.Create((NullReferenceException ex) => ex.Message);
            
            try
            {
                throw new NullReferenceException();
            }
            catch (Exception ex) when (contextHandler.ShouldCatch(ex))
            {
                Assert.True(true);
            }

            Assert.Throws<InvalidEnumArgumentException>(() =>
            {
                try
                {
                    throw new InvalidEnumArgumentException();
                }
                catch (Exception ex) when (contextHandler.ShouldCatch(ex))
                {
                    Assert.True(false);
                }
            });
        }
    }
}