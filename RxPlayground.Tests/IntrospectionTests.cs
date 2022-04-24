using Microsoft.VisualStudio.TestTools.UnitTesting;
using RxPlayground.RxInteractive;
using System;
using System.Linq;
using System.Reactive.Linq;

namespace RxPlayground.Tests
{
    [TestClass]
    public class IntrospectionTests
    {
        public static readonly IObservable<int> Source = Observable.Range(0, 10);

        [TestMethod]
        public void Return() => Observable.Return(0).Inspect(new());

        [TestMethod]
        public void Select() => Source.Select(x => x + "!").Inspect(new());

        [TestMethod]
        public void Where() => Source.Where(x => x > 5).Inspect(new());

        [TestMethod]
        public void CombineLatest2() => Observable
            .CombineLatest(
                new[]
                {
                    Observable.Return(0),
                    Observable.Return(1),
                    Observable.Return(2),
                    Observable.Return(3)
                },
                nums => nums.Sum()
            )
            .Inspect(new());

        [TestMethod]
        public void CombineLatest3() => Observable
            .CombineLatest(
                Observable.Return("x"),
                Observable.Return(0),
                (s, n) => s + n
            )
            .Inspect(new());

        [TestMethod]
        public void CombineLatest4() => Observable
            .CombineLatest(
                Observable.Return("x"),
                Observable.Return(true),
                Observable.Return(0),
                (s, b, n) => s + b + n
            )
            .Inspect(new());

        [TestMethod]
        public void TestCache()
        {
            var xSquared_ = Source.Select(x => x * x);
            var cache = new IIntrospectionCache();
            var xSquaredBelow10_ = xSquared_.Where(x => x < 10);
            var xSquaredAbove10_ = xSquared_.Where(x => x > 10);

            xSquaredAbove10_.Inspect(cache);
            xSquaredBelow10_.Inspect(cache);

            Assert.IsTrue(cache.InteractiveObservables.ContainsKey(xSquared_));
            Assert.IsTrue(cache.InteractiveObservables.ContainsKey(xSquaredBelow10_));
            Assert.IsTrue(cache.InteractiveObservables.ContainsKey(xSquaredAbove10_));

            Assert.AreEqual(2, cache.InteractiveObservables[xSquared_].Downstreams.Count);
            Assert.AreEqual(cache.InteractiveObservables[xSquaredAbove10_], cache.InteractiveObservables[xSquared_].Downstreams[0].Target);
            Assert.AreEqual(cache.InteractiveObservables[xSquaredBelow10_], cache.InteractiveObservables[xSquared_].Downstreams[1].Target);

            Assert.AreEqual(1, cache.InteractiveObservables[xSquared_].Upstreams.Count);
            Assert.AreEqual(cache.InteractiveObservables[Source], cache.InteractiveObservables[xSquared_].Upstreams[0].Owner);

            Assert.AreEqual(0, cache.InteractiveObservables[xSquaredAbove10_].Downstreams.Count);
            Assert.AreEqual(1, cache.InteractiveObservables[xSquaredAbove10_].Upstreams.Count);
            Assert.AreEqual(cache.InteractiveObservables[xSquared_], cache.InteractiveObservables[xSquaredAbove10_].Upstreams[0].Owner);

            Assert.AreEqual(0, cache.InteractiveObservables[xSquaredBelow10_].Downstreams.Count);
            Assert.AreEqual(1, cache.InteractiveObservables[xSquaredBelow10_].Upstreams.Count);
            Assert.AreEqual(cache.InteractiveObservables[xSquared_], cache.InteractiveObservables[xSquaredBelow10_].Upstreams[0].Owner);
        }
    }
}