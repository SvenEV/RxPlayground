using System.Collections.Immutable;
using System.Reflection;

namespace RxPlayground.RxInteractive
{
    public static class Introspection
    {
        public static ImmutableDictionary<string, IInteractiveObservable> GetParentsThroughReflection<T>(IObservable<T> observable)
        {
            var fields = observable.GetType()
                .GetFields(BindingFlags.Instance | BindingFlags.NonPublic);

            return fields
                .Select(field => new { FieldName = field.Name, Value = field.GetValue(observable) })
                .Where(o => o.Value is IInteractiveObservable)
                .ToImmutableDictionary(o => o.FieldName, o => (IInteractiveObservable)o.Value!);
        }

        public static IInteractiveObservable<T> Inspect<T>(this IObservable<T> observable) =>
            (IInteractiveObservable<T>)InjectInspector(observable);

        public static IInteractiveObservable InjectInspector(object observable)
        {
            if (observable is IInteractiveObservable interactiveObservable)
            {
                // Observable is already interactive => just return it, don't wrap it in another IInteractiveObservable
                return interactiveObservable;
            }

            try
            {
                return MakeInteractiveByConvention(observable);
            }
            catch (Exception ex)
            {
                var type = observable.GetType();
                var genericTypeDef = type.GetGenericTypeDefinition();

                var typeParamNames = genericTypeDef.GetGenericArguments().Select(t => t.Name).ToImmutableList();
                var typeArgs = Enumerable.Range(0, typeParamNames.Count).ToImmutableDictionary(i => typeParamNames[i], i => type.GenericTypeArguments[i]);
                var typeName = genericTypeDef.FullName![0..genericTypeDef.FullName!.LastIndexOf('`')] + "<" + string.Join(", ", typeParamNames) + ">";

                var fields = type
                    .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
                    .ToImmutableDictionary(f => f.Name, f => f.GetValue(observable));

                return typeName switch
                {
                    // TODO: Implement special cases for observable types that cannot be mapped by convention
                    _ => throw new NotImplementedException($"'{typeName}' cannot be made interactive by convention, but is also not special-cased", ex)
                };
            }
        }

        /// <summary>
        /// Tries to make an observable interactive by:
        /// - automatically determining the 'T' in <see cref="IObservable{T}"/>
        ///   (the observable must implement <see cref="IObservable{T}"/> for exactly one type T)
        /// - automatically cloning the observable recursively, replacing any upstream
        ///   <see cref="IObservable{T}"/> with an <see cref="IInteractiveObservable{T}"/>
        /// </summary>
        public static IInteractiveObservable MakeInteractiveByConvention(object observable)
        {
            var observableElementType = observable.GetType().GetImplementationsOfGenericTypeDef(typeof(IObservable<>)) switch
            {
                { Count: 1 } list => list[0].GenericTypeArguments[0]!,
                var list => throw new ArgumentException($"'{observable.GetType().Name}' must have exactly 1 implementation of IObservable<T> but has {list.Count}")
            };

            return MakeInteractiveObservable(observableElementType, AutoCloneObservable(observable));
        }

        /// <summary>
        /// Automatically maps fields "_foo" to constructor parameters "foo", thereby recursively calling
        /// InjectInspector() on fields of type IObservable<> or IEnumerable<IObservable<>>
        /// </summary>
        static object AutoCloneObservable(object observable)
        {
            try
            {
                var type = observable.GetType();

                var fieldsTransformed = type
                    .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
                    .ToImmutableDictionary(f => f.Name, f => f.GetValue(observable) switch
                    {
                        null => null,
                        var value when IsObservableType(value.GetType()) => InjectInspector(value),
                        var value when IsObservableCollectionType(value.GetType()) => ReflectionUtil.DynamicMap(value, InjectInspector),
                        var value => value
                    });

                var constructor = type!.GetConstructors()
                    .FirstOrDefault(ctor => ctor.GetParameters().Length == fieldsTransformed.Count)
                    ?? throw new ArgumentException($"Constructor with {fieldsTransformed.Count} parameters not found");

                var constructorArgs = constructor.GetParameters()
                    .Select(para => fieldsTransformed!.TryGetValue("_" + para.Name, out var value)
                        ? value
                        : throw new Exception($"Constructor parameter '{para.Name}' has no matching field '_{para.Name}'")
                    )
                    .ToArray();

                return constructor.Invoke(constructorArgs);
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

        static IInteractiveObservable MakeInteractiveObservable(Type observableElementType, object observable) =>
            (IInteractiveObservable)Activator.CreateInstance(typeof(InteractiveObservable<>).MakeGenericType(observableElementType), observable)!;

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
