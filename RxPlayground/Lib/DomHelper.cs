using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System.Numerics;

namespace RxPlayground.Lib
{
    public class DomHelper : JsModule
    {
        public DomHelper(IJSRuntime js) : base(GetDocumentReferenceAsync(js))
        {
        }

        private static async Task<IJSObjectReference> GetDocumentReferenceAsync(IJSRuntime js)
        {
            return await js.InvokeAsync<IJSObjectReference>("import", "./js/dom-helper.js");
        }

        public async Task SetPointerCaptureAsync(ElementReference targetElement, long pointerId)
        {
            await this.InvokeVoidAsync("setPointerCapture", targetElement, pointerId);
        }

        public async Task ReleasePointerCaptureAsync(ElementReference targetElement, long pointerId)
        {
            await this.InvokeVoidAsync("releasePointerCapture", targetElement, pointerId);
        }

        public async Task<Vector2> GetElementSizeAsync(ElementReference element)
        {
            var size = await this.InvokeAsync<float[]>("getElementSize", element);
            return new(size[0], size[1]);
        }
    }
}
