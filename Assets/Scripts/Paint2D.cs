// NOT USED IN FINAL VERSION. WAS TESTING OUT PAINTING FUNCTIONALITY IN 2D SPACE.

using UnityEngine;
using UnityEngine.UI;

public class Paint2D : MonoBehaviour
{
    public RawImage canvasImage; 
    public Texture2D paintTexture;
    public RectTransform brush; 

    private Color currentColor = Color.blue;
    private float hue = 0.5f; 
    private const float hueChangeThreshold = 300f; 
    private const float tiltCompensationFactor = 0.1f; 
    private Vector2 smoothPosition = Vector2.zero;
    private const float smoothingFactor = 0.2f; 
    private const float axOffset = 0f; 
    private const float ayOffset = 0f;

    private const float axRangeScale = 1.5f; 
    private const float ayRangeScale = 1.5f;

    private void Start()
    {

        paintTexture = new Texture2D(1024, 1024);
        Color fillColor = Color.white;
        Color[] fillPixels = new Color[paintTexture.width * paintTexture.height];
        for (int i = 0; i < fillPixels.Length; i++) fillPixels[i] = fillColor;
        paintTexture.SetPixels(fillPixels);
        paintTexture.Apply();

        canvasImage.texture = paintTexture;
    }

    private Color GetDynamicBrushColor()
    {

        return Color.HSVToRGB(hue, 1f, 1f);
    }

    public void Paint(Vector2 position)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvasImage.rectTransform,
            RectTransformUtility.WorldToScreenPoint(null, position),
            null,
            out Vector2 localPoint
        );

        float canvasWidth = canvasImage.rectTransform.rect.width;
        float canvasHeight = canvasImage.rectTransform.rect.height;

        float normalizedX = (localPoint.x / canvasWidth + 0.5f) * paintTexture.width;
        float normalizedY = (localPoint.y / canvasHeight + 0.5f) * paintTexture.height;

        int xCenter = Mathf.Clamp((int)normalizedX, 0, paintTexture.width - 1);
        int yCenter = Mathf.Clamp((int)normalizedY, 0, paintTexture.height - 1);

        Color brushColor = GetDynamicBrushColor();

        int brushRadius = 10; // Radius of the brush
        for (int y = -brushRadius; y <= brushRadius; y++)
        {
            for (int x = -brushRadius; x <= brushRadius; x++)
            {
                if (x * x + y * y <= brushRadius * brushRadius)
                {
                    int pixelX = xCenter + x;
                    int pixelY = yCenter + y;

                    if (pixelX >= 0 && pixelX < paintTexture.width && pixelY >= 0 && pixelY < paintTexture.height)
                    {
                        paintTexture.SetPixel(pixelX, pixelY, brushColor);
                    }
                }
            }
        }

        paintTexture.Apply();
    }

    public void Update()
    {
        if (MqttReceiver.Instance != null)
        {
            var motionData = MqttReceiver.Instance.player1MotionData;
            float adjustedAx = (motionData.ax - axOffset) * axRangeScale;
            float adjustedAy = (motionData.ay - ayOffset + motionData.gx * tiltCompensationFactor) * ayRangeScale;

            Vector2 screenPosition = MapToScreenDimensions(
                adjustedAx, adjustedAy,
                -32768, 32767,
                -32768, 32767, 
                canvasImage.rectTransform.rect.width, 
                canvasImage.rectTransform.rect.height
            );

            smoothPosition.x = Mathf.Lerp(smoothPosition.x, screenPosition.x, smoothingFactor);
            smoothPosition.y = Mathf.Lerp(smoothPosition.y, screenPosition.y, smoothingFactor);
            brush.anchoredPosition = smoothPosition;
            Paint(brush.position);
        }
    }

    private Vector2 MapToScreenDimensions(float x, float y, float xMin, float xMax, float yMin, float yMax, float screenWidth, float screenHeight)
    {
        float normalizedX = Mathf.InverseLerp(xMin, xMax, x);
        float normalizedY = Mathf.InverseLerp(yMin, yMax, y);

        float mappedX = (normalizedX - 0.5f) * screenWidth;
        float mappedY = (normalizedY - 0.5f) * screenHeight;

        return new Vector2(mappedX, mappedY);
    }
}