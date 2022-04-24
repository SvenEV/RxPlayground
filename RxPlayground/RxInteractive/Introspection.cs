using System.Collections.Immutable;

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
    }

    public static class Introspection
    {
        public static IInteractiveObservable<T> Inspect<T>(this IObservable<T> observable, IIntrospectionCache cache) =>
            (IInteractiveObservable<T>)InjectInspector(observable, cache);

        public static IInteractiveObservable InjectInspector(object observable, IIntrospectionCache cache)
        {
            return cache.GetOrAdd(observable, CreateInteractiveObservable);

            IInteractiveObservable CreateInteractiveObservable()
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
                        return (IInteractiveObservable)Activator.CreateInstance(
                            typeof(InteractiveObservable<>).MakeGenericType(typeArgs["T"]),
                            observable,
                            ImmutableList<IInteractiveObservablePort>.Empty
                        )!;

                    case "System.Reactive.Subjects.ConnectableObservable<TSource, TResult>":
                        {
                            var source = type.GetAllPrivateFields()
                            .Single(f => f.Name == "_source")
                            .GetValue(observable)!;

                            var sourceWrapped = InjectInspector(source, cache).AddDownstream();

                            var subject = type.GetAllPrivateFields()
                                .Single(f => f.Name == "_subject")
                                .GetValue(observable)!;

                            return (IInteractiveObservable)Activator.CreateInstance(
                                typeof(InteractiveObservable<>).MakeGenericType(typeArgs["TResult"]),
                                type.GetConstructors()[0].Invoke(new[] { sourceWrapped, subject })!,
                                ImmutableList.Create(sourceWrapped)
                            )!;
                        }

                    case "System.Reactive.Linq.ObservableImpl.RefCount<TSource>":
                        {
                            var source = type.GetAllPrivateFields()
                                .Single(f => f.Name == "_source")
                                .GetValue(observable)!;

                            var sourceWrapped = InjectInspector(source, cache).AddDownstream();

                            var minObservers = type.GetAllPrivateFields()
                                .Single(f => f.Name == "_minObservers")
                                .GetValue(observable)!;

                            return (IInteractiveObservable)Activator.CreateInstance(
                                typeof(InteractiveObservable<>).MakeGenericType(typeArgs["TSource"]),
                                type.GetConstructors()[0].Invoke(new[] { sourceWrapped, minObservers })!,
                                ImmutableList.Create(sourceWrapped)
                            )!;
                        }


                    default:
                        return TryByConvention(typeName);
                };
            }

            IInteractiveObservable TryByConvention(string typeName)
            {
                try
                {
                    return MakeInteractiveByConvention(observable, cache);
                }
                catch (Exception ex)
                {
                    throw new NotImplementedException($"'{typeName}' is not special-cased, but can also not be made interactive by convention", ex);
                }
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
        public static IInteractiveObservable MakeInteractiveByConvention(object observable, IIntrospectionCache cache)
        {
            var observableElementType = observable.GetType().GetImplementationsOfGenericTypeDef(typeof(IObservable<>)) switch
            {
                { Count: 1 } list => list[0].GenericTypeArguments[0]!,
                var list => throw new ArgumentException($"'{observable.GetType().Name}' must have exactly 1 implementation of IObservable<T> but has {list.Count}")
            };

            try
            {
                var type = observable.GetType();

                var initialFieldsAndUpstreams = new
                {
                    Fields = ImmutableDictionary<string, object?>.Empty,
                    Upstreams = ImmutableList<IInteractiveObservablePort>.Empty
                };

                var fieldsAndUpstreams = type
                    .GetAllPrivateFields()
                    .Aggregate(initialFieldsAndUpstreams, (state, f) => f.GetValue(observable) switch
                    {
                        null =>
                            state with { Fields = state.Fields.Add(f.Name, null) },

                        var value when (
                            IsObservableType(value.GetType()) &&
                            InjectInspector(value, cache) is var upstream &&
                            upstream.AddDownstream() is var upstreamPort
                        ) =>
                            state with
                            {
                                Fields = state.Fields.Add(f.Name, upstreamPort),
                                Upstreams = state.Upstreams.Add(upstreamPort)
                            },

                        var value when (
                            IsObservableCollectionType(value.GetType()) &&
                            ReflectionUtil.DynamicMap(value, obs => InjectInspector(obs, cache).AddDownstream()) is var upstreamPorts
                        ) =>
                            state with
                            {
                                Fields = state.Fields.Add(f.Name, upstreamPorts),
                                Upstreams = state.Upstreams.AddRange(upstreamPorts.Cast<IInteractiveObservablePort>().ToImmutableList())
                            },

                        var value =>
                            state with { Fields = state.Fields.Add(f.Name, value) },
                    });

                var constructor = type!.GetConstructors()
                    .FirstOrDefault(ctor => ctor.GetParameters().Length == fieldsAndUpstreams.Fields.Count)
                    ?? throw new ArgumentException($"'{type!.FullName}' has no constructor with {fieldsAndUpstreams.Fields.Count} parameter(s) (for field(s) {string.Join(", ", fieldsAndUpstreams.Fields.Keys)})");

                var constructorArgs = constructor.GetParameters()
                    .Select(para => fieldsAndUpstreams.Fields.TryGetValue("_" + para.Name, out var value)
                        ? value
                        : throw new Exception($"Constructor parameter '{para.Name}' has no matching field '_{para.Name}'")
                    )
                    .ToArray();

                var observableCloned = constructor.Invoke(constructorArgs);

                var interactiveObservable = (IInteractiveObservable)Activator.CreateInstance(
                    typeof(InteractiveObservable<>).MakeGenericType(observableElementType),
                    observableCloned,
                    fieldsAndUpstreams.Upstreams)!;

                return interactiveObservable;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to auto-clone '{observable.GetType().Name}'", ex);
            }
        }

        static object CloneObservable(Type type, object args)
        {
            var props = args.GetType().GetProperties().ToImmutableDictionary(prop => prop.Name, prop => prop.GetValue(args));

            var constructor = type.GetConstructors()
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
