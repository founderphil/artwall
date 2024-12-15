// NOT USED IN THE FINAL PROJECT

using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;
using System.Linq;

public class Tokenizer
{
    private Dictionary<string, int> vocab; // Vocabulary
    private Dictionary<int, string> reverseVocab; // Reverse vocabulary
    private string[] merges;  // BPE merges

    public Tokenizer(TextAsset vocabFile, TextAsset mergesFile)
    {
        if (vocabFile == null || mergesFile == null)
        {
            Debug.LogError("Vocabulary or merges file is missing. Check your TextAsset assignments.");
            return;
        }

        // Load vocab.json
        vocab = JsonConvert.DeserializeObject<Dictionary<string, int>>(vocabFile.text);
        reverseVocab = vocab.ToDictionary(kvp => kvp.Value, kvp => kvp.Key);

        // Load merges.txt
        merges = mergesFile.text.Split('\n').Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();

        Debug.Log($"Loaded vocabulary size: {vocab.Count}");
        Debug.Log($"Loaded merges count: {merges.Length}");
    }

    public List<int> Encode(string inputText)
    {
        if (string.IsNullOrEmpty(inputText))
        {
            Debug.LogError("Input text is null or empty.");
            return new List<int>();
        }

        // Preprocess text
        inputText = PreprocessText(inputText);

        // Tokenize text into individual characters
        List<string> tokens = inputText.Select(c => c.ToString()).ToList();

        // Apply BPE merges
        ApplyMerges(tokens);

        // Map tokens to vocab IDs
        List<int> tokenIds = new List<int>();
        foreach (var token in tokens)
        {
            if (vocab.TryGetValue(token, out int id))
            {
                tokenIds.Add(id);
            }
            else
            {
                Debug.LogWarning($"Token '{token}' not found in vocabulary. Using <|unk|>.");
                if (vocab.TryGetValue("<|unk|>", out int unkId))
                {
                    tokenIds.Add(unkId); // Use <|unk|> for unknown tokens
                }
                else
                {
                    Debug.LogError("<|unk|> token is missing from the vocabulary.");
                    tokenIds.Add(0); // Fallback to token ID 0
                }
            }
        }

        Debug.Log($"Encoded Tokens: {string.Join(", ", tokenIds)}");
        return tokenIds;
    }

    public string Decode(List<int> tokens)
    {
        List<string> decodedWords = new List<string>();

        foreach (int token in tokens)
        {
            if (reverseVocab.TryGetValue(token, out string word))
            {
                decodedWords.Add(word);
            }
            else
            {
                Debug.LogWarning($"Token ID '{token}' not found in reverse vocabulary. Skipping...");
            }
        }

        // Join decoded words, replacing "Ġ" with spaces
        string decodedText = string.Join("", decodedWords).Replace("Ġ", " ").Trim();
        Debug.Log($"Decoded Text: {decodedText}");
        return decodedText;
    }

    private void ApplyMerges(List<string> tokens)
    {
        foreach (var merge in merges)
        {
            string[] pair = merge.Split(' ');
            int index = 0;

            while (index >= 0)
            {
                index = tokens.IndexOf(pair[0], index);
                if (index != -1 && index < tokens.Count - 1 && tokens[index + 1] == pair[1])
                {
                    tokens[index] = tokens[index] + tokens[index + 1];
                    tokens.RemoveAt(index + 1);
                }
                if (index != -1) index++;
            }
        }
    }

    private string PreprocessText(string text)
    {
        text = " " + text.Trim();

        text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");

        Debug.Log($"Preprocessed Text: {text}");
        return text;
    }

    
    public void TestTokenizer()
    {
        string testText = "The quick brown fox jumps over the lazy dog.";
        Debug.Log($"Testing Tokenizer with input: \"{testText}\"");

        List<int> encodedTokens = Encode(testText);
        Debug.Log($"Encoded Tokens: {string.Join(", ", encodedTokens)}");

        string decodedText = Decode(encodedTokens);
        Debug.Log($"Decoded Text: \"{decodedText}\"");

        if (decodedText.Trim() == testText.Trim())
        {
            Debug.Log("Tokenizer Test Passed: The decoded text matches the original input.");
        }
        else
        {
            Debug.LogError("Tokenizer Test Failed: The decoded text does not match the original input.");
        }
    }
}