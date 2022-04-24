using System.Collections;
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
        InjectInspectorResult GetOrAdd(object observable, Func<InjectInspectorResult> factoryFunc);

        bool ShouldInspect(object observable);
    }

    public record InjectInspectorResult(
        object ObservableRaw,
        ImmutableList<IInteractiveObservable> Upstreams)
    {
        public object TryCreatePort() => ObservableRaw is IInteractiveObservable interactiveObservable
            ? interactiveObservable.AddDownstream()
            : ObservableRaw;
    }

    public static class Introspection
    {
        private static InjectInspectorResult MakeInteractiveOrPassThrough(
            bool shouldInspect,
            object observable,
            params InjectInspectorResult[] upstreamResults)
        {
            if (shouldInspect)
            {
                var elementType = GetObservableElementType(observable);

                var interactiveObs = IInteractiveObservable.Create(
                    elementType,
                    observable,
                    upstreamResults.SelectMany(res => res.Upstreams.Select(up => up.AddDownstream())).ToImmutableList());

                // TODO: Eliminate the AddDownstream() call here because ports are already created beforehand.
                // They need to be provided to this method, but only if shouldInspect==true, hm.....

                return new InjectInspectorResult(interactiveObs, ImmutableList.Create(interactiveObs));
            }
            else
            {
                return new InjectInspectorResult(observable, upstreamResults.SelectMany(res => res.Upstreams).ToImmutableList());
            }
        }

        public static InjectInspectorResult Inspect<T>(this IObservable<T> observable, IIntrospectionCache cache) =>
            InjectInspector(observable, cache);

        public static InjectInspectorResult InjectInspector(object observable, IIntrospectionCache cache)
        {
            return cache.GetOrAdd(observable, CreateInteractiveObservable);

            InjectInspectorResult CreateInteractiveObservable()
            {
                var type = observable.GetType();
                var genericTypeDef = type.IsConstructedGenericType ? type.GetGenericTypeDefinition() : null;
                var typeParamNames = genericTypeDef?.GetGenericArguments().Select(t => t.Name).ToImmutableList() ?? ImmutableList<string>.Empty;
                var typeArgs = Enumerable.Range(0, typeParamNames.Count).ToImmutableDictionary(i => typeParamNames[i], i => type.GenericTypeArguments[i]);

                var typeName = type.IsConstructedGenericType
                    ? genericTypeDef!.FullName![0..genericTypeDef.FullName!.LastIndexOf('`')] + "<" + string.Join(", ", typeParamNames) + ">"
                    : type.FullName!;

                var shouldInspect = cache.ShouldInspect(observable);

                switch (typeName)
                {
                    case "System.Reactive.Subjects.Subject<T>":
                        return MakeInteractiveOrPassThrough(shouldInspect, observable);

                    case "System.Reactive.Subjects.ConnectableObservable<TSource, TResult>":
                        {
                            var source = type.GetAllPrivateFields()
                                .Single(f => f.Name == "_source")
                                .GetValue(observable)!;

                            var sourceInstrumented = InjectInspector(source, cache);

                            var subject = type.GetAllPrivateFields()
                                .Single(f => f.Name == "_subject")
                                .GetValue(observable)!;

                            return MakeInteractiveOrPassThrough(shouldInspect,
                                Instantiate(type, new { source = sourceInstrumented.TryCreatePort(), subject = subject }),
                                sourceInstrumented
                            );
                        }

                    case "System.Reactive.Linq.ObservableImpl.RefCount<TSource>":
                        {
                            var source = type.GetAllPrivateFields()
                                .Single(f => f.Name == "_source")
                                .GetValue(observable)!;

                            var sourceInstrumented = InjectInspector(source, cache);

                            var minObservers = type.GetAllPrivateFields()
                                .Single(f => f.Name == "_minObservers")
                                .GetValue(observable)!;

                            return MakeInteractiveOrPassThrough(shouldInspect,
                                Instantiate(type, new { source = sourceInstrumented.TryCreatePort(), minObservers = minObservers }),
                                sourceInstrumented
                            );
                        }


                    default:
                        return TryByConvention(shouldInspect, typeName);
                };
            }

            InjectInspectorResult TryByConvention(bool shouldInspect, string typeName)
            {
                try
                {
                    return MakeInteractiveByConvention(shouldInspect, observable, cache);
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
        public static InjectInspectorResult MakeInteractiveByConvention(bool shouldInspect, object observable, IIntrospectionCache cache)
        {
            Type observableElementType = GetObservableElementType(observable);

            try
            {
                var type = observable.GetType();

                //var initialFieldsAndUpstreams = new
                //{
                //    Fields = ImmutableDictionary<string, object?>.Empty,
                //    Upstreams = ImmutableList<IInteractiveObservablePort>.Empty
                //};

                var fields = new Dictionary<string, object?>();
                var upstreamResults = new List<InjectInspectorResult>();

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
                        fields.Add(field.Name, observableInstrumented.TryCreatePort());
                        upstreamResults.Add(observableInstrumented);
                    }
                    else if (IsObservableCollectionType(value.GetType()))
                    {
                        var observablesInstrumented = ((IEnumerable)value).Cast<object>()
                            .Select(v => InjectInspector(v, cache))
                            .ToList();

                        fields.Add(field.Name, ReflectionUtil.CreateList(value.GetType(), observablesInstrumented.Select(o => o.TryCreatePort())));
                        upstreamResults.AddRange(observablesInstrumented);
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

                var clonedObservable = constructor.Invoke(constructorArgs);

                return MakeInteractiveOrPassThrough(
                    shouldInspect,
                    clonedObservable,
                    upstreamResults.ToArray()
                );
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to auto-clone '{observable.GetType().Name}'", ex);
            }
        }

        private static Type GetObservableElementType(object observable) =>
            observable.GetType().GetImplementationsOfGenericTypeDef(typeof(IObservable<>)) switch
            {
                { Count: 1 } list => list[0].GenericTypeArguments[0]!,
                var list => throw new ArgumentException($"'{observable.GetType().Name}' must have exactly 1 implementation of IObservable<T> but has {list.Count}")
            };

        private static object Instantiate(Type type, object args)
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
