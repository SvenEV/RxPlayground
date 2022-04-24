export function getDocument() {
    return document;
}

export function setPointerCapture(targetElement, pointerId) {
    targetElement.setPointerCapture(pointerId);
}

export function releasePointerCapture(targetElement, pointerId) {
    targetElement.releasePointerCapture(pointerId);
}