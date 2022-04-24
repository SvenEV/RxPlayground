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
        public void Return() => Observable.Return(0).Inspect();

        [TestMethod]
        public void Select() => Source.Select(x => x + "!").Inspect();

        [TestMethod]
        public void Where() => Source.Where(x => x > 5).Inspect();

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
            .Inspect();

        [TestMethod]
        public void CombineLatest3() => Observable
            .CombineLatest(
                Observable.Return("x"),
                Observable.Return(0),
                (s, n) => s + n
            )
            .Inspect();

        [TestMethod]
        public void CombineLatest4() => Observable
            .CombineLatest(
                Observable.Return("x"),
                Observable.Return(true),
                Observable.Return(0),
                (s, b, n) => s + b + n
            )
            .Inspect();
    }
}