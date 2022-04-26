export function getScaleFactors(element) {
    const scaleX = element.parentElement.clientWidth / element.clientWidth;
    const scaleY = element.parentElement.clientHeight / element.clientHeight;
    return { scaleX, scaleY };
}