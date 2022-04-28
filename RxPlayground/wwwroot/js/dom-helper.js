export function getDocument() {
    return document;
}

export function setPointerCapture(targetElement, pointerId) {
    targetElement.setPointerCapture(pointerId);
}

export function releasePointerCapture(targetElement, pointerId) {
    targetElement.releasePointerCapture(pointerId);
}

export function getElementSize(element) {
    return element ? [element.offsetWidth, element.offsetHeight] : [0, 0];
}