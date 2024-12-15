using System.Collections.Generic;
using UnityEngine;
using Unity.Sentis;
using System.Text;
using Unity.Collections;
using TMPro;
using System.Collections;


public class RunWhisper : MonoBehaviour
{
    // Sentis workers for Whisper
    Worker decoder1, decoder2, encoder, spectrogram, argmax;

    public GameObject flyingWordPrefab; 
    public Transform spawnPoint; 
    public Transform wordPlane; 

    public TMP_FontAsset imprintedFont; 
    public float wordFadeDuration = 20f;
    public float wordFlightSpeed = 10f;
    public TextAsset jsonFile;

    private float planeHalfWidth;
    private float planeHalfLength;
    public TextMeshPro outputText;
    const int maxTokens = 100;
    const int END_OF_TEXT = 50257;
    const int START_OF_TRANSCRIPT = 50258;
    const int ENGLISH = 50259;
    const int TRANSCRIBE = 50359;
    const int TRANSLATE = 50358;  // Not used in this context but i want to save it for future use. add chinese or something
    const int NO_TIME_STAMPS = 50363;

    // Token management
    int tokenCount = 0;
    NativeArray<int> outputTokens;
    string[] tokens;

    // Audio processing
    int numSamples;
    const int maxSamples = 30 * 16000;
    Tensor<float> encodedAudio;
    Tensor<float> audioInput;
    bool transcribe = false;
    string outputString = "";

    // Model assets
    public ModelAsset audioDecoder1, audioDecoder2;
    public ModelAsset audioEncoder;
    public ModelAsset logMelSpectro;

    // Internal variables
    Awaitable m_Awaitable;
    NativeArray<int> lastToken;
    Tensor<int> lastTokenTensor;
    Tensor<int> tokensTensor;

    public float lifespan = 5f; 
    int[] whiteSpaceCharacters = new int[256];

    void Start()
    {
        Initialize();
        planeHalfWidth = 5f * wordPlane.localScale.x; 
        planeHalfLength = 5f * wordPlane.localScale.z;  
        Debug.Log($"flyingWordPrefab (Start): {flyingWordPrefab}");
        Debug.Log($"Instantiating flyingWordPrefab: {flyingWordPrefab.name} (Instance ID: {flyingWordPrefab.GetInstanceID()})");
    }

    public void Initialize()
    {
        SetupWhiteSpaceShifts();
        GetTokens();

        // Load models and create workers
        decoder1 = new Worker(ModelLoader.Load(audioDecoder1), BackendType.GPUCompute);
        decoder2 = new Worker(ModelLoader.Load(audioDecoder2), BackendType.GPUCompute);
        encoder = new Worker(ModelLoader.Load(audioEncoder), BackendType.GPUCompute);
        spectrogram = new Worker(ModelLoader.Load(logMelSpectro), BackendType.GPUCompute);

        // Create an argmax worker
        FunctionalGraph graph = new FunctionalGraph();
        var input = graph.AddInput(DataType.Float, new DynamicTensorShape(1, 1, 51865));
        var amax = Functional.ArgMax(input, -1, false);
        var selectTokenModel = graph.Compile(amax);
        argmax = new Worker(selectTokenModel, BackendType.GPUCompute);

        // Initialize output tokens
        outputTokens = new NativeArray<int>(maxTokens, Allocator.Persistent);
    }

    public async void ProcessAudioClip(AudioClip clip)
    {
        // Reset variables for new processing
        if (tokensTensor != null) tokensTensor.Dispose();
        outputString = "";
        transcribe = true;

        // Initialize tokens
        outputTokens[0] = START_OF_TRANSCRIPT;
        outputTokens[1] = ENGLISH;
        outputTokens[2] = TRANSCRIBE;
        tokenCount = 2;

        // Initialize last token
        lastToken = new NativeArray<int>(1, Allocator.Persistent);
        lastToken[0] = NO_TIME_STAMPS;
        lastTokenTensor = new Tensor<int>(new TensorShape(1, 1), new[] { NO_TIME_STAMPS });

        // Prepare tokens tensor
        tokensTensor = new Tensor<int>(new TensorShape(1, maxTokens));
        ComputeTensorData.Pin(tokensTensor);

        // Load and encode the audio clip
        audioClip = clip;
        LoadAudio();
        EncodeAudio();

        // Upload initial tokens
        tokensTensor.Reshape(new TensorShape(1, tokenCount));
        tokensTensor.dataOnBackend.Upload<int>(outputTokens, tokenCount);

        // Start transcription loop
        while (true)
        {
            if (!transcribe || tokenCount >= (outputTokens.Length - 1))
                break;

            m_Awaitable = InferenceStep();
            await m_Awaitable;
        }
    }

    AudioClip audioClip;

    void LoadAudio()
    {
        numSamples = maxSamples;
        var data = new float[numSamples];
        audioClip.GetData(data, 0);
        audioInput = new Tensor<float>(new TensorShape(1, numSamples), data);
    }

    void EncodeAudio()
    {
        spectrogram.Schedule(audioInput);
        var logmel = spectrogram.PeekOutput() as Tensor<float>;
        encoder.Schedule(logmel);
        encodedAudio = encoder.PeekOutput() as Tensor<float>;
    }

    async Awaitable InferenceStep()
    {
        decoder1.SetInput("input_ids", tokensTensor);
        decoder1.SetInput("encoder_hidden_states", encodedAudio);
        decoder1.Schedule();

        var pastKeyValues = new Dictionary<string, Tensor<float>>();
        for (int i = 0; i < 4; i++)
        {
            pastKeyValues[$"past_key_values.{i}.decoder.key"] = decoder1.PeekOutput($"present.{i}.decoder.key") as Tensor<float>;
            pastKeyValues[$"past_key_values.{i}.decoder.value"] = decoder1.PeekOutput($"present.{i}.decoder.value") as Tensor<float>;
            pastKeyValues[$"past_key_values.{i}.encoder.key"] = decoder1.PeekOutput($"present.{i}.encoder.key") as Tensor<float>;
            pastKeyValues[$"past_key_values.{i}.encoder.value"] = decoder1.PeekOutput($"present.{i}.encoder.value") as Tensor<float>;
        }

        decoder2.SetInput("input_ids", lastTokenTensor);
        foreach (var kvp in pastKeyValues)
        {
            decoder2.SetInput(kvp.Key, kvp.Value);
        }
        decoder2.Schedule();

        // Get logits and perform argmax
        var logits = decoder2.PeekOutput("logits") as Tensor<float>;
        argmax.Schedule(logits);
        using var t_Token = await argmax.PeekOutput().ReadbackAndCloneAsync() as Tensor<int>;
        int index = t_Token[0];

        // Update tokens
        tokenCount++;
        outputTokens[tokenCount] = lastToken[0];
        lastToken[0] = index;
        tokensTensor.Reshape(new TensorShape(1, tokenCount));
        tokensTensor.dataOnBackend.Upload<int>(outputTokens, tokenCount);
        lastTokenTensor.dataOnBackend.Upload<int>(lastToken, 1);

        // Update transcription
        if (index == END_OF_TEXT)
        {
            transcribe = false;
        }
        else if (index < tokens.Length)
        {
            string newText = GetUnicodeText(tokens[index]);
            outputString += newText;

            string[] newWords = newText.Split(new char[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
            foreach (var w in newWords)
            {
                if (!string.IsNullOrEmpty(w))
                {
                    SpawnFlyingWord(w);
                }
            }
            
        }
        outputText.text = outputString;
        Debug.Log($"Transcription updated: {outputString}");
    }

    void SpawnFlyingWord(string word) // Spawn a flying word from prefab
{
    if (string.IsNullOrEmpty(word) || flyingWordPrefab == null)
    {
        Debug.LogError("Missing prefab or invalid word for spawning.");
        return;
    }

    // Instantiate a new clone of the prefab
    GameObject flyingWordInstance = Instantiate(flyingWordPrefab, spawnPoint.position, Quaternion.identity);
    WordFly wordFly = flyingWordInstance.GetComponent<WordFly>();

    if (wordFly != null)
    {
        Vector3 targetPosition = wordPlane.position + new Vector3(
            UnityEngine.Random.Range(-planeHalfWidth, planeHalfWidth), 0,
            UnityEngine.Random.Range(-planeHalfLength, planeHalfLength));
        
        wordFly.Initialize(word, targetPosition, wordFlightSpeed, this, flyingWordInstance);
    }
    else
    {
        Debug.LogError("The flyingWordPrefab is missing the WordFly script.");
    }
}

    public void CreateImprintedWordOnPlane(string word) // Create an imprinted word on the plane
{
    if (string.IsNullOrEmpty(word) || wordPlane == null)
    {
        Debug.LogError("Missing word or wordPlane reference for imprinting.");
        return;
    }

    float wordPlaneZ = wordPlane.position.z;
    float planeWidth = wordPlane.localScale.x * 10f; 
    float planeHeight = wordPlane.localScale.y * 10f;
    float randomX = UnityEngine.Random.Range(-planeWidth / 2, planeWidth / 2) + wordPlane.position.x;
    float randomY = UnityEngine.Random.Range(-planeHeight / 2, planeHeight / 2) + wordPlane.position.y;

    Vector3 randomPosition = new Vector3(randomX, randomY, wordPlaneZ);

    GameObject imprintedWord = new GameObject($"Imprinted_{word}");
    imprintedWord.transform.position = randomPosition;
    imprintedWord.transform.SetParent(wordPlane);

    float randomRotationZ = UnityEngine.Random.Range(-45f, 45f);
    imprintedWord.transform.rotation = Quaternion.Euler(0f, 0f, randomRotationZ);

    TextMeshPro tmp = imprintedWord.AddComponent<TextMeshPro>();
    tmp.text = word;
    tmp.fontSize = 100f;
    tmp.color = Color.white;
    tmp.alignment = TextAlignmentOptions.Center;

    // Assign font asset
    if (imprintedFont != null)
    {
        tmp.font = imprintedFont;
    }

    tmp.ForceMeshUpdate();

    Vector2 textBounds = tmp.GetRenderedValues(false);
    float scaleFactor = Mathf.Min(200f / textBounds.x, 50f / textBounds.y);
    imprintedWord.transform.localScale = new Vector3(scaleFactor, scaleFactor, scaleFactor);

    tmp.enableAutoSizing = true;
    tmp.fontSizeMin = 1f;
    tmp.fontSizeMax = 10f;

    StartCoroutine(FadeOutSpawnText(tmp, wordFadeDuration));
}

    IEnumerator FadeOutSpawnText(TextMeshPro textComponent, float duration)
{
    if (gameObject.scene.name == null)
    {
        Debug.LogError($"Attempted to destroy prefab: {gameObject.name}");
        yield break;
    }

    Color originalColor = textComponent.color;
    float elapsed = 0;

    while (elapsed < duration)
    {
        elapsed += Time.deltaTime;
        float alpha = Mathf.Lerp(1f, 0f, elapsed / duration);
        textComponent.color = new Color(originalColor.r, originalColor.g, originalColor.b, alpha);
        yield return null;
    }

    if (textComponent != null)
    {
        Destroy(textComponent.gameObject);
        Debug.Log($"{gameObject.name} Destroy coming from FadeOutSpawnText");
    }
}

   void GetTokens()
    {
        var vocab = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, int>>(jsonFile.text);
        tokens = new string[vocab.Count];
        foreach (var item in vocab)
        {
            tokens[item.Value] = item.Key;
        }
    }
    string GetUnicodeText(string text)
    {
        var bytes = Encoding.GetEncoding("ISO-8859-1").GetBytes(ShiftCharacterDown(text));
        return Encoding.UTF8.GetString(bytes);
    }

    string ShiftCharacterDown(string text)
    {
        string outText = "";
        foreach (char letter in text)
        {
            outText += ((int)letter <= 256) ? letter :
                (char)whiteSpaceCharacters[(int)(letter - 256)];
        }
        return outText;
    }

    void SetupWhiteSpaceShifts()
    {
        for (int i = 0, n = 0; i < 256; i++)
        {
            if (IsWhiteSpace((char)i)) whiteSpaceCharacters[n++] = i;
        }
    }

    bool IsWhiteSpace(char c)
    {
        return !(('!' <= c && c <= '~') || ('¡' <= c && c <= '¬') || ('®' <= c && c <= 'ÿ'));
    }

    private void OnDestroy()
    {
        Debug.Log($"{gameObject.name} OnDestroy called.");
        decoder1?.Dispose();
        decoder2?.Dispose();
        encoder?.Dispose();
        spectrogram?.Dispose();
        argmax?.Dispose();
        tokensTensor?.Dispose();
        lastTokenTensor?.Dispose();
        if (audioInput != null) audioInput.Dispose();
        if (encodedAudio != null) encodedAudio.Dispose();
        if (outputTokens.IsCreated) outputTokens.Dispose();
        if (lastToken.IsCreated) lastToken.Dispose();
        Debug.Log($"Destroy called on: {gameObject.name}");
    }
}
