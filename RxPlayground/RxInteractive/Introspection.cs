using System.Collections;
using System.Collections.Immutable;
using System.Reflection;

namespace RxPlayground.RxInteractive
{
    /// <summary>
    /// Keeps track of <see cref="IInteractiveObservable"/> instances.
    /// This is needed in case an observable is visited multiple times, e.g. when constructing
    /// two subscriptions to the same observable.
    /// </summary>
    public interface IIntrospectionCache
    {
        IInteractiveObservable GetOrAdd(object observable, Func<IInteractiveObservable> factoryFunc);

        bool ShouldInspect(object observable);
    }

    public record InjectInspectorResult(
        object ObservableRaw,
        ImmutableList<IInteractiveObservablePort> Upstreams);

    public static class Introspection
    {
        public static InjectInspectorResult Inspect<T>(this IObservable<T> observable, IIntrospectionCache cache) =>
            InjectInspector(observable, cache);

        public static InjectInspectorResult InjectInspector(object observable, IIntrospectionCache cache)
        {
            var shouldInspect = cache.ShouldInspect(observable);
            var rebuiltObservable = RebuildObservable();

            if (shouldInspect)
            {
                var interactiveObs = cache.GetOrAdd(observable, () =>
                    IInteractiveObservable.Create(rebuiltObservable.ObservableRaw, rebuiltObservable.Upstreams)
                );

                var port = interactiveObs.AddDownstream();
                return new InjectInspectorResult(port, ImmutableList.Create(port));
            }
            else
            {
                return new InjectInspectorResult(rebuiltObservable.ObservableRaw, rebuiltObservable.Upstreams);
            }

            InjectInspectorResult RebuildObservable()
            {
                var type = observable.GetType();
                var genericTypeDef = type.IsConstructedGenericType ? type.GetGenericTypeDefinition() : null;
                var typeParamNames = genericTypeDef?.GetGenericArguments().Select(t => t.Name).ToImmutableList() ?? ImmutableList<string>.Empty;
                var typeArgs = Enumerable.Range(0, typeParamNames.Count).ToImmutableDictionary(i => typeParamNames[i], i => type.GenericTypeArguments[i]);

                var typeName = type.IsConstructedGenericType
                    ? genericTypeDef!.FullName![0..genericTypeDef.FullName!.LastIndexOf('`')] + "<" + string.Join(", ", typeParamNames) + ">"
                    : type.FullName!;

                switch (typeName)
                {
                    case "System.Reactive.Subjects.Subject<T>":
                        return new InjectInspectorResult(observable, ImmutableList<IInteractiveObservablePort>.Empty);

                    case "System.Reactive.Subjects.BehaviorSubject<T>":
                        return new InjectInspectorResult(observable, ImmutableList<IInteractiveObservablePort>.Empty);

                    case "System.Reactive.Subjects.ReplaySubject<T>":
                        return new InjectInspectorResult(observable, ImmutableList<IInteractiveObservablePort>.Empty);

                    case "System.Reactive.Subjects.ConnectableObservable<TSource, TResult>":
                        {
                            var source = type.GetAllPrivateFields()
                                .Single(f => f.Name == "_source")
                                .GetValue(observable)!;

                            var subject = type.GetAllPrivateFields()
                                .Single(f => f.Name == "_subject")
                                .GetValue(observable)!;

                            var sourceInstrumented = InjectInspector(source, cache);

                            return new InjectInspectorResult(
                                Instantiate(type, new { source = sourceInstrumented.ObservableRaw, subject }),
                                sourceInstrumented.Upstreams);
                        }

                    case "System.Reactive.Linq.ObservableImpl.RefCount<TSource>":
                        {
                            var source = type.GetAllPrivateFields()
                                .Single(f => f.Name == "_source")
                                .GetValue(observable)!;

                            var minObservers = type.GetAllPrivateFields()
                                .Single(f => f.Name == "_minObservers")
                                .GetValue(observable)!;

                            var sourceInstrumented = InjectInspector(source, cache);

                            return new InjectInspectorResult(
                                Instantiate(type, new { source = sourceInstrumented.ObservableRaw, minObservers }),
                                sourceInstrumented.Upstreams);
                        }

                    case "System.Reactive.Linq.ObservableImpl.AutoConnect<T>":
                        {
                            var source = type.GetAllPrivateFields()
                                .Single(f => f.Name == "_source")
                                .GetValue(observable)!;

                            var minObservers = type.GetAllPrivateFields()
                                .Single(f => f.Name == "_minObservers")
                                .GetValue(observable)!;

                            var onConnect = type.GetAllPrivateFields()
                                .Single(f => f.Name == "_onConnect")
                                .GetValue(observable)!;

                            var sourceInstrumented = InjectInspector(source, cache);

                            return new InjectInspectorResult(
                                Instantiate(type, new { source = sourceInstrumented.ObservableRaw, minObservers, onConnect }),
                                sourceInstrumented.Upstreams);
                        }


                    default:
                        try
                        {
                            return MakeInteractiveByConvention(shouldInspect, observable, cache);
                        }
                        catch (Exception ex)
                        {
                            throw new NotImplementedException($"'{typeName}' is not special-cased, but can also not be made interactive by convention", ex);
                        }
                };
            }
        }

        /// <summary>
        /// Tries to make an observable interactive by:
        /// - automatically determining the 'T' in <see cref="IObservable{T}"/>
        ///   (the observable must implement <see cref="IObservable{T}"/> for exactly one type T)
        /// - automatically cloning the observable recursively, replacing any upstream
        ///   <see cref="IObservable{T}"/> with an <see cref="IInteractiveObservable{T}"/>
        ///   (this relies on the convention that there is a 1-to-1 mapping of private fields "_foo" to constructor parameters "foo")
        /// </summary>
        public static InjectInspectorResult MakeInteractiveByConvention(bool shouldInspect, object observable, IIntrospectionCache cache)
        {
            Type observableElementType = GetObservableElementType(observable);

            try
            {
                var type = observable.GetType();
                var fields = new Dictionary<string, object?>();

                var upstreamPorts = new List<IInteractiveObservablePort>();

                foreach (var field in type.GetAllPrivateFields())
                {
                    var value = field.GetValue(observable);

                    if (value is null)
                    {
                        fields.Add(field.Name, value);
                    }
                    else if (IsObservableType(value.GetType()))
                    {
                        var observableInstrumented = InjectInspector(value, cache);
                        fields.Add(field.Name, observableInstrumented.ObservableRaw);
                        upstreamPorts.AddRange(observableInstrumented.Upstreams);
                    }
                    else if (IsObservableCollectionType(value.GetType()))
                    {
                        var observablesInstrumented = ((IEnumerable)value).Cast<object>()
                            .Select(v => InjectInspector(v, cache))
                            .ToList();

                        fields.Add(field.Name, ReflectionUtil.CreateList(value.GetType(), observablesInstrumented.Select(o => o.ObservableRaw)));
                        upstreamPorts.AddRange(observablesInstrumented.SelectMany(o => o.Upstreams));
                    }
                    else
                    {
                        fields.Add(field.Name, value);
                    }
                }

                var constructor = type!.GetConstructors()
                    .FirstOrDefault(ctor => ctor.GetParameters().Length == fields.Count)
                    ?? throw new ArgumentException($"'{type!.FullName}' has no constructor with {fields.Count} parameter(s) (for field(s) {string.Join(", ", fields.Keys)})");

                var constructorArgs = constructor.GetParameters()
                    .Select(para => fields.TryGetValue("_" + para.Name, out var value)
                        ? value
                        : throw new Exception($"Constructor parameter '{para.Name}' has no matching field '_{para.Name}'")
                    )
                    .ToArray();

                var newObservable = constructor.Invoke(constructorArgs);
                return new(newObservable, upstreamPorts.ToImmutableList());
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to auto-clone '{observable.GetType().Name}'", ex);
            }
        }

        public static Type GetObservableElementType(object observable) =>
            observable.GetType().GetImplementationsOfGenericTypeDef(typeof(IObservable<>)) switch
            {
                { Count: 1 } list => list[0].GenericTypeArguments[0]!,
                var list => throw new ArgumentException($"'{observable.GetType().Name}' must have exactly 1 implementation of IObservable<T> but has {list.Count}")
            };

        private static object Instantiate(Type type, object args)
        {
            var props = args.GetType().GetProperties().ToImmutableDictionary(prop => prop.Name, prop => prop.GetValue(args));

            var constructor = type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault(ctor => ctor.GetParameters().Select(para => para.Name).OrderBy(s => s).SequenceEqual(props.Keys.OrderBy(s => s)))
                ?? throw new ArgumentException($"Constructor not found: '{type.Name}({string.Join(", ", props.Keys)})'");


            var constructorArgs = constructor.GetParameters().Select(para => props[para.Name!]).ToArray();
            return constructor.Invoke(constructorArgs);
        }


        public static bool IsObservableType(Type type) =>
            type.IsAssignableToGenericType(typeof(IObservable<>));

        // sloppy implementation: Something like IObservable<int>[] or List<IObservable<int>> works,
        // but something like Foo<string, bool> where Foo<T1, T2> : IEnumerable<IObservable<int>> does not.
        public static bool IsObservableCollectionType(Type type) =>
            (type.IsArray &&
                type.GetElementType() is Type elementType &&
                IsObservableType(elementType)) ||
            (type.IsAssignableToGenericType(typeof(IEnumerable<>)) &&
                type.GetGenericArguments() is var typeArgs &&
                typeArgs.Length == 1 &&
                IsObservableType(typeArgs[0]));


    }
}
