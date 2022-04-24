using System.Collections;
using System.Collections.Immutable;
using System.Reflection;

namespace RxPlayground.RxInteractive
{
    public static class ReflectionUtil
    {
        /// <summary>
        /// Returns the concrete generic interface types that <paramref name="givenType"/> implements
        /// and that are constructed from <paramref name="genericInterfaceType"/>.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        public static ImmutableList<Type> GetImplementationsOfGenericTypeDef(this Type givenType, Type genericInterfaceType)
        {
            if (givenType.IsGenericTypeDefinition)
                throw new ArgumentException("Must not be a generic type definition", nameof(givenType));

            if (!genericInterfaceType.IsGenericTypeDefinition)
                throw new ArgumentException("Must be a generic type definition", nameof(genericInterfaceType));

            return (givenType.IsConstructedGenericType && givenType.GetGenericTypeDefinition() == genericInterfaceType)
                ? ImmutableList.Create(givenType)
                : givenType.GetImplementedInterfaces()
                    .Where(interfaceType => interfaceType.IsConstructedGenericType && interfaceType.GetGenericTypeDefinition() == genericInterfaceType)
                    .ToImmutableList();
        }

        public static bool IsAssignableToGenericType(this Type givenType, Type genericType)
        {
            // adapted from https://stackoverflow.com/a/5461399
            return (givenType.IsGenericType && givenType.GetGenericTypeDefinition() == genericType) ||
                givenType.GetImplementedInterfaces().Any(it => it.IsGenericType && it.GetGenericTypeDefinition() == genericType);
        }

        public static IEnumerable<Type> GetImplementedInterfaces(this Type type)
        {
            foreach (var interfaceType in type.GetInterfaces())
                yield return interfaceType;

            // TODO: below part not needed??
            //if (type.BaseType is Type baseType)
            //{
            //    foreach (var interfaceType in GetImplementedInterfaces(baseType))
            //        yield return interfaceType;
            //}
        }

        public static IEnumerable<FieldInfo> GetAllPrivateFields(this Type type)
        {
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic))
                yield return field;

            if (type.BaseType is Type baseType)
                foreach (var field in baseType.GetAllPrivateFields())
                    yield return field;
        }

        /// <summary>
        /// Applies <paramref name="mapper"/> to all elements of the given <see cref="IEnumerable{T}"/>
        /// and returns a <see cref="List{T}"/> with the results.
        /// </summary>
        public static IEnumerable DynamicMap(object enumerable, Func<object, object> mapper)
        {
            var type = enumerable.GetType();

            var elementType = type.IsArray
                ? type.GetElementType()!
                : type.GetGenericArguments()[0];

            var list = Activator.CreateInstance(typeof(List<>).MakeGenericType(elementType))!;
            var addMethod = list.GetType().GetMethod("Add")!;

            foreach (var element in (IEnumerable<object>)enumerable)
            {
                var mapped = mapper(element);
                addMethod.Invoke(list, new[] { mapped });
            }

            return (IEnumerable)list;
        }
    }
}
