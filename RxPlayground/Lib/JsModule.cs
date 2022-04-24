using Microsoft.JSInterop;
using System.Diagnostics.CodeAnalysis;

namespace RxPlayground.Lib
{
    public abstract class JsModule : IJSObjectReference
    {
        private readonly Task<IJSObjectReference> moduleTask;

        public JsModule(Task<IJSObjectReference> loadModuleAsync)
        {
            moduleTask = loadModuleAsync;
        }

        public async ValueTask DisposeAsync()
        {
            var module = await moduleTask;
            await module.DisposeAsync();
        }

        public async ValueTask<TValue> InvokeAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicProperties)] TValue>(string identifier, object?[]? args)
        {
            var module = await moduleTask;
            return await module.InvokeAsync<TValue>(identifier, args);
        }

        public async ValueTask<TValue> InvokeAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicProperties)] TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            var module = await moduleTask;
            return await module.InvokeAsync<TValue>(identifier, cancellationToken, args);
        }
    }
}
