using System.Collections.Generic;
using UnityEngine;
using TMPro;
using Unity.Sentis;

public class LocalLLMProcessor : MonoBehaviour
{
    public RunWhisper runWhisper; // Reference to Whisper transcription system
    public TextMeshProUGUI responseText; // UI element to display response
    public ModelAsset modelAsset;
    public TextAsset vocabFile;
    public TextAsset mergesFile;

    private Tokenizer tokenizer;
    private Worker llmWorker;

    private const int maxTokens = 50;
    private const int END_OF_TEXT = 50258;
    private bool isProcessing = false;
    private string lastProcessedText = "";
    private float debounceTime = 1.5f; // Time to wait for transcription to settle
    private float lastTranscriptionUpdate = 0f;

    void Start()
    {
        Debug.Log("LLM Processor Initialized");

        if (modelAsset == null)
        {
            Debug.LogError("ModelAsset is not assigned.");
            return;
        }

        // Load model
        Model model = ModelLoader.Load(modelAsset);

        // Check input tensor names and shapes
        foreach (var input in model.inputs)
        {
            Debug.Log($"Model Input: {input.name}, Shape: {input.shape}");
        }

        llmWorker = new Worker(model, BackendType.GPUCompute);

        // Load tokenizer resources
        if (vocabFile == null || mergesFile == null)
        {
            Debug.LogError("vocab.json or merges.txt is missing. Ensure they are assigned.");
            return;
        }

        tokenizer = new Tokenizer(vocabFile, mergesFile);
    }

    void Update()
    {
        if (runWhisper == null || isProcessing)
            return;

        string currentTranscription = runWhisper.outputText.text;

        // Ignore unchanged transcriptions
        if (string.IsNullOrEmpty(currentTranscription) || currentTranscription == lastProcessedText)
            return;

        // Check for finalized transcription based on punctuation
        if (currentTranscription.EndsWith(".") || currentTranscription.EndsWith("?") || currentTranscription.EndsWith("!"))
        {
            lastProcessedText = currentTranscription;
            Debug.Log($"Final Transcription Detected: {lastProcessedText}");
            GenerateResponse(lastProcessedText);
            return;
        }

        // Update transcription time for debounce handling
        lastProcessedText = currentTranscription;
        lastTranscriptionUpdate = Time.time;

        // Timeout fallback: Process transcription after debounce time
        if (!isProcessing && Time.time - lastTranscriptionUpdate > debounceTime)
        {
            Debug.Log($"Timeout reached. Processing transcription: {lastProcessedText}");
            GenerateResponse(lastProcessedText);
        }
    }

    void GenerateResponse(string inputText)
    {
        if (string.IsNullOrWhiteSpace(inputText))
        {
            Debug.LogWarning("GenerateResponse called with empty or invalid input.");
            return;
        }

        Debug.Log($"Generating response for: {inputText}");
        isProcessing = true;

        Tensor<int> inputTensor = null;
        Tensor<int> attentionMaskTensor = null;
        Tensor<int> positionIdsTensor = null;
        Tensor<float> logits = null;

        // Initialize past key-values for GPT
        Tensor<float>[] pastKeyValues = new Tensor<float>[12]; // 6 layers, each with a key and value
        for (int i = 0; i < pastKeyValues.Length; i++)
        {
            pastKeyValues[i] = new Tensor<float>(new TensorShape(1, 12, 0, 64)); // Adjust dimensions as needed
        }

        try
        {
            // Build the prompt
            string prompt = $"You are a helpful assistant. Respond thoughtfully to the user's input.\n\nUser: {inputText}\nAssistant:";
            List<int> inputTokens = tokenizer.Encode(prompt);
            Debug.Log($"Prompt Tokens: {string.Join(", ", inputTokens)}");

            // Prepare input tensors
            inputTensor = new Tensor<int>(new TensorShape(1, inputTokens.Count), inputTokens.ToArray());

            int[] attentionMask = new int[inputTokens.Count];
            for (int i = 0; i < attentionMask.Length; i++) attentionMask[i] = 1;
            attentionMaskTensor = new Tensor<int>(new TensorShape(1, attentionMask.Length), attentionMask);

            int[] positionIds = new int[inputTokens.Count];
            for (int i = 0; i < positionIds.Length; i++) positionIds[i] = i;
            positionIdsTensor = new Tensor<int>(new TensorShape(1, positionIds.Length), positionIds);

            // Set input tensors
            llmWorker.SetInput("input_ids", inputTensor);
            llmWorker.SetInput("attention_mask", attentionMaskTensor);
            llmWorker.SetInput("position_ids", positionIdsTensor);

            for (int i = 0; i < pastKeyValues.Length; i++)
            {
                llmWorker.SetInput($"past_key_values.{i / 2}.{(i % 2 == 0 ? "key" : "value")}", pastKeyValues[i]);
            }

            // Run inference
            llmWorker.Schedule();

            // Retrieve logits and updated past key-values
            logits = llmWorker.PeekOutput("logits") as Tensor<float>;
            if (logits == null)
            {
                Debug.LogError("Logits tensor is null.");
                return;
            }

            for (int i = 0; i < pastKeyValues.Length; i++)
            {
                pastKeyValues[i]?.Dispose();
                pastKeyValues[i] = llmWorker.PeekOutput($"present.{i / 2}.{(i % 2 == 0 ? "key" : "value")}") as Tensor<float>;
            }

            // Decode logits into tokens
            List<int> outputTokens = DecodeTokens(logits, inputTokens, END_OF_TEXT, 1.5f); // Apply penalties to avoid repetition
            string outputText = tokenizer.Decode(outputTokens);

            Debug.Log($"Final Decoded Response: {outputText}");
            responseText.text = outputText;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Error during GenerateResponse: {ex.Message}");
        }
        finally
        {
            inputTensor?.Dispose();
            attentionMaskTensor?.Dispose();
            positionIdsTensor?.Dispose();
            logits?.Dispose();
            foreach (var tensor in pastKeyValues)
            {
                tensor?.Dispose();
            }
            isProcessing = false;
        }
    }

    List<int> DecodeTokens(Tensor<float> logits, List<int> inputTokens, int vocabSize, float penalty = 1.5f)
    {
        List<int> outputTokens = new List<int>();
        HashSet<int> penalizedTokens = new HashSet<int>();

        for (int i = 0; i < maxTokens; i++)
        {
            float[] logitsArray = logits.DownloadToArray();
            int nextToken = ArgMaxWithPenalty(logitsArray, penalizedTokens, penalty, vocabSize);

            // Stop decoding if END_OF_TEXT is reached
            if (nextToken == END_OF_TEXT)
                break;

            // Add token to the output and penalize it
            outputTokens.Add(nextToken);
            penalizedTokens.Add(nextToken);
            inputTokens.Add(nextToken);
        }

        return outputTokens;
    }

    int ArgMaxWithPenalty(float[] logits, HashSet<int> penalizedTokens, float penalty, int vocabSize)
    {
        int maxIndex = 0;
        float maxValue = float.MinValue;

        for (int i = 0; i < vocabSize; i++)
        {
            float value = penalizedTokens.Contains(i) ? logits[i] - penalty : logits[i];

            if (value > maxValue)
            {
                maxValue = value;
                maxIndex = i;
            }
        }

        return maxIndex;
    }
    

    void OnDestroy()
    {
        llmWorker?.Dispose();
    }
}