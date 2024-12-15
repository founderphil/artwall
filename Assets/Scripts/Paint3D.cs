using UnityEngine;

public class Paint3D : MonoBehaviour
{
    [Header("General Setup")]
    public RenderTexture paintTexture;
    public Material paintMaterial;
    public Transform plane;

    [Header("Player 1 Setup")]
    public Material player1Material;   
    public GameObject player1Brush3DObject;
    public GameObject player1SecondarySphere;

    [Header("Player 2 Setup")]
    public Material player2Material;   
    public GameObject player2Brush3DObject;
    public GameObject player2SecondarySphere;

    [Header("Player 3 Setup")]
    public Material player3Material;
    public GameObject player3Brush3DObject;
    public GameObject player3SecondarySphere;

    // Brightness cycle
    private float[] brightnessLevels = new float[] { 1.0f, 0.40f, 0.60f, 0.80f };

    // Player states
    private Color player1Color;
    private Color player2Color;
    private Color player3Color;

    private int player1BrightnessIndex = 0;
    private int player2BrightnessIndex = 0;
    private int player3BrightnessIndex = 0;

    private float player1Brightness;
    private float player2Brightness;
    private float player3Brightness;

    // Shake to change controls
    private const float shakeThreshold = 15000f;
    private float shakeDisableTime = 0.5f;

    private float player1ShakeTimer = 0f;
    private float player2ShakeTimer = 0f;
    private float player3ShakeTimer = 0f;

    // Smoothing positions
    private Vector2 smoothPosPlayer1 = Vector2.zero;
    private Vector2 smoothPosPlayer2 = Vector2.zero;
    private Vector2 smoothPosPlayer3 = Vector2.zero;

    private const float smoothingFactor = 0.15f;

    private float lastAz1 = 0f;
    private float lastAz2 = 0f;
    private float lastAz3 = 0f;

    private Texture2D paintTexture2D;

    // offsets
    private float axOffset = 0f;
    private float ayOffset = 0f;
    private const float axRangeScale = 1.5f;
    private const float ayRangeScale = 1.5f;

    private void Start()
    {
        // init player colors
        player1Color = (player1Material != null) ? player1Material.color : new Color(0.6078f, 0.7490f, 0.8705f);
        player2Color = (player2Material != null) ? player2Material.color : Color.yellow;
        player3Color = (player3Material != null) ? player3Material.color : Color.red;

        // 1f brightness to start for all players
        player1Brightness = brightnessLevels[player1BrightnessIndex];
        player2Brightness = brightnessLevels[player2BrightnessIndex];
        player3Brightness = brightnessLevels[player3BrightnessIndex];

        // Create and clear painting texture, on 2d plane
        paintTexture2D = new Texture2D(2048, 2048, TextureFormat.RGBA32, false);
        Color[] fillPixels = new Color[paintTexture2D.width * paintTexture2D.height];
        for (int i = 0; i < fillPixels.Length; i++)
        {
            fillPixels[i] = Color.white;
        }
        paintTexture2D.SetPixels(fillPixels);
        paintTexture2D.Apply();

        Graphics.Blit(paintTexture2D, paintTexture);
        paintMaterial.mainTexture = paintTexture;
    }

    private void Update()
    {
        if (MqttReceiver.Instance == null) return;

        // subscribe to MQTT broker for all player data
        var p1Data = MqttReceiver.Instance.player1MotionData;
        var p2Data = MqttReceiver.Instance.player2MotionData;
        var p3Data = MqttReceiver.Instance.player3MotionData;

        // Handle shakes and brightness changes for each player
        HandlePlayerShakeAndBrightness(ref lastAz1, ref player1ShakeTimer, ref player1Brightness, ref player1BrightnessIndex, p1Data.az);
        HandlePlayerShakeAndBrightness(ref lastAz2, ref player2ShakeTimer, ref player2Brightness, ref player2BrightnessIndex, p2Data.az);
        HandlePlayerShakeAndBrightness(ref lastAz3, ref player3ShakeTimer, ref player3Brightness, ref player3BrightnessIndex, p3Data.az);

        // Convert motion data to UV and smooth for every player
        Vector2 uv1 = ConvertToUV(p1Data.ax, p1Data.ay);
        Vector2 uv2 = ConvertToUV(p2Data.ax, p2Data.ay);
        Vector2 uv3 = ConvertToUV(p3Data.ax, p3Data.ay);

        // smooth player position
        smoothPosPlayer1 = Vector2.Lerp(smoothPosPlayer1, uv1, smoothingFactor);
        smoothPosPlayer2 = Vector2.Lerp(smoothPosPlayer2, uv2, smoothingFactor);
        smoothPosPlayer3 = Vector2.Lerp(smoothPosPlayer3, uv3, smoothingFactor);

        // Paint, if not shaking
        if (Time.time >= player1ShakeTimer) PaintAtUV(smoothPosPlayer1, player1Color, player1Brightness);
        if (Time.time >= player2ShakeTimer) PaintAtUV(smoothPosPlayer2, player2Color, player2Brightness);
        if (Time.time >= player3ShakeTimer) PaintAtUV(smoothPosPlayer3, player3Color, player3Brightness);

        // Position player paintballs
        PositionBrushOnPlane(smoothPosPlayer1, player1Brush3DObject);
        PositionBrushOnPlane(smoothPosPlayer2, player2Brush3DObject);
        PositionBrushOnPlane(smoothPosPlayer3, player3Brush3DObject);

        // Update player sphere signifiers
        UpdateSphereColor(player1Brush3DObject, player1SecondarySphere, player1Color * player1Brightness);
        UpdateSphereColor(player2Brush3DObject, player2SecondarySphere, player2Color * player2Brightness);
        UpdateSphereColor(player3Brush3DObject, player3SecondarySphere, player3Color * player3Brightness);
    }

    private void HandlePlayerShakeAndBrightness(ref float lastAz, ref float shakeTimer, ref float brightness, ref int brightnessIndex, float currentAz)
    {
        float azDelta = Mathf.Abs(currentAz - lastAz);
        if (azDelta > shakeThreshold && Time.time >= shakeTimer)
        {
            // Cycle brightness
            brightnessIndex++;
            if (brightnessIndex >= brightnessLevels.Length) brightnessIndex = 0;
            brightness = brightnessLevels[brightnessIndex];
            Debug.Log("Brush brightness changed to: " + (brightness * 100) + "%");
            shakeTimer = Time.time + shakeDisableTime;
        }
        lastAz = currentAz;
    }

    private Vector2 ConvertToUV(int ax, int ay)
    {
        float adjustedAx = (ax - axOffset) * axRangeScale;
        float adjustedAy = (ay - ayOffset) * ayRangeScale;

        float normalizedX = Mathf.InverseLerp(-32768, 32767, adjustedAx);
        float normalizedY = Mathf.InverseLerp(-32768, 32767, adjustedAy);

        return new Vector2(normalizedX, normalizedY);
    }

    private void PaintAtUV(Vector2 uv, Color baseColor, float brightness)
    {
        int x = Mathf.Clamp((int)(uv.x * paintTexture2D.width), 0, paintTexture2D.width - 1);
        int y = Mathf.Clamp((int)(uv.y * paintTexture2D.height), 0, paintTexture2D.height - 1);

        Color adjustedColor = baseColor * brightness;
        int brushRadius = 50;

        for (int offsetY = -brushRadius; offsetY <= brushRadius; offsetY++)
        {
            for (int offsetX = -brushRadius; offsetX <= brushRadius; offsetX++)
            {
                if (offsetX * offsetX + offsetY * offsetY <= brushRadius * brushRadius)
                {
                    int px = x + offsetX;
                    int py = y + offsetY;
                    if (px >= 0 && px < paintTexture2D.width && py >= 0 && py < paintTexture2D.height)
                    {
                        paintTexture2D.SetPixel(px, py, adjustedColor);
                    }
                }
            }
        }

        paintTexture2D.Apply();
        Graphics.Blit(paintTexture2D, paintTexture);
    }

    private void PositionBrushOnPlane(Vector2 uv, GameObject brushObj)
    {
        if (brushObj == null) return;

        float correctedU = 1f - uv.x;
        float correctedV = 1f - uv.y;

        float finalX = Mathf.Lerp(300f, 750f, correctedU);
        float finalY = Mathf.Lerp(0f, 220f, correctedV);
        float finalZ = plane.transform.position.z; 

        brushObj.transform.position = new Vector3(finalX, finalY, finalZ);
    }

    private void UpdateSphereColor(GameObject brushObj, GameObject secondarySphere, Color currentColor)
    {
        if (brushObj != null)
        {
            Renderer br = brushObj.GetComponent<Renderer>();
            if (br != null)
            {
                br.material.color = currentColor;
            }
        }

        if (secondarySphere != null)
        {
            Renderer sr = secondarySphere.GetComponent<Renderer>();
            if (sr != null)
            {
                sr.material.color = currentColor;
            }
        }
    }
}