using System.IO;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;


public class AudioRecorder : MonoBehaviour
{
    public Button speakButton;
    public TextMeshProUGUI statusText;
    public RunWhisper runWhisperScript;

    private AudioClip recordedClip;
    private bool isRecording = false;
    private string filePath;
    public Toggle autoListenToggle;
    public CanvasGroup speakButtonCanvasGroup;
    private float silenceThreshold = 0.01f; // 1% of max amplitude
    private float silenceTime = 0f;
    private float requiredSilence = 1.5f; 
    private bool autoListening = false;
    private const int sampleRate = 16000;
    private int micPosition = 0;
    private AudioClip continuousClip;

    void Start()
    {
        speakButton.onClick.AddListener(OnSpeakButtonClick);
        filePath = Path.Combine(Application.dataPath, "Data/recordedAudio.wav");
        statusText.text = "Press to speak.";

        autoListenToggle.onValueChanged.AddListener(OnAutoListenChanged);
    }
    void OnAutoListenChanged(bool isOn)
{
    if (isOn)
    {
        // Disabled speak button
        speakButton.interactable = false;
        speakButtonCanvasGroup.alpha = 0.2f;
        StartContinuousRecording();
    }
    else
    {
        // Enabled speak button
        speakButton.interactable = true;
        speakButtonCanvasGroup.alpha = 1f;
        StopContinuousRecording();
    }
}

void StartContinuousRecording()
{
    autoListening = true;
    continuousClip = Microphone.Start(null, true, 30, sampleRate);
    statusText.text = "Auto-listening...";
}

void StopContinuousRecording()
{
    autoListening = false;
    if (Microphone.IsRecording(null))
        Microphone.End(null);
    silenceTime = 0f;
    statusText.text = "Auto-listen stopped.";
}

void Update()
{
    if (autoListening)
    {
        MonitorSilenceAndCapture();
    }
}

void MonitorSilenceAndCapture()
{
    if (continuousClip == null) return;

    int currentPosition = Microphone.GetPosition(null);
    int length = currentPosition - micPosition;
    if (length < 0) length += continuousClip.samples;

    float[] samples = new float[length];
    continuousClip.GetData(samples, micPosition);
    micPosition = currentPosition;

    float sum = 0f;
    for (int i = 0; i < samples.Length; i++)
    {
        sum += Mathf.Abs(samples[i]);
    }
    float avgAmplitude = (length > 0) ? sum / length : 0f;

    if (avgAmplitude < silenceThreshold)
    {
        silenceTime += Time.deltaTime;
    }
    else
    {
        silenceTime = 0f;
    }

    if (silenceTime > requiredSilence)
    {
        SaveCurrentClipSegmentAndProcess();
        silenceTime = 0f;
    }
}


void SaveCurrentClipSegmentAndProcess()
{   //get phrase data
    int phraseLength = Mathf.Min(5 * sampleRate, continuousClip.samples);
    float[] phraseData = new float[phraseLength];
    continuousClip.GetData(phraseData, Mathf.Max(0, micPosition - phraseLength));
    //create audio clip
    AudioClip phraseClip = AudioClip.Create("Phrase", phraseLength, 1, sampleRate, false);
    phraseClip.SetData(phraseData, 0);
    //send to Whisper
    runWhisperScript.ProcessAudioClip(phraseClip);
    statusText.text = "Processing phrase...";
}

    void OnSpeakButtonClick()
    {
        if (isRecording)
        {
            StopRecording();
        }
        else
        {
            StartRecording();
        }
    }

    void StartRecording()
    {
        recordedClip = Microphone.Start(null, false, 10, 16000); // 10 seconds max, 16kHz
        isRecording = true;
        statusText.text = "Recording...";
    }

    void StopRecording()
    {
        if (isRecording)
        {
            Microphone.End(null);
            isRecording = false;
            statusText.text = "Recording stopped.";
            SaveRecordingToFile();
            runWhisperScript.ProcessAudioClip(recordedClip);
        }
    }

    void SaveRecordingToFile()
    {
        if (recordedClip == null)
        {
            Debug.LogError("No audio recorded.");
            return;
        }

        var samples = new float[recordedClip.samples * recordedClip.channels];
        recordedClip.GetData(samples, 0);

        using (var fileStream = CreateEmpty(filePath))
        {
            ConvertAndWrite(fileStream, samples);
            WriteHeader(fileStream, recordedClip);
        }

        Debug.Log("Audio file saved at: " + filePath);
        statusText.text = "Recording saved.";
    }

    FileStream CreateEmpty(string path)
    {
        var fileStream = new FileStream(path, FileMode.Create);
        for (int i = 0; i < 44; i++)
        {
            fileStream.WriteByte(0);
        }
        return fileStream;
    }

    void ConvertAndWrite(FileStream fileStream, float[] samples)
    {
        short[] intData = new short[samples.Length];
        byte[] bytesData = new byte[samples.Length * 2];

        int rescaleFactor = 32767;
        for (int i = 0; i < samples.Length; i++)
        {
            intData[i] = (short)(samples[i] * rescaleFactor);
            byte[] byteArr = BitConverter.GetBytes(intData[i]);
            byteArr.CopyTo(bytesData, i * 2);
        }

        fileStream.Write(bytesData, 0, bytesData.Length);
    }

    void WriteHeader(FileStream fileStream, AudioClip clip)
    {
        int hz = clip.frequency;
        int channels = clip.channels;
        int samples = clip.samples;

        fileStream.Seek(0, SeekOrigin.Begin);

        byte[] riff = System.Text.Encoding.UTF8.GetBytes("RIFF");
        fileStream.Write(riff, 0, 4);

        byte[] chunkSize = BitConverter.GetBytes(fileStream.Length - 8);
        fileStream.Write(chunkSize, 0, 4);

        byte[] wave = System.Text.Encoding.UTF8.GetBytes("WAVE");
        fileStream.Write(wave, 0, 4);

        byte[] fmt = System.Text.Encoding.UTF8.GetBytes("fmt ");
        fileStream.Write(fmt, 0, 4);

        byte[] subChunk1 = BitConverter.GetBytes(16);
        fileStream.Write(subChunk1, 0, 4);

        byte[] audioFormat = BitConverter.GetBytes((short)1);
        fileStream.Write(audioFormat, 0, 2);

        byte[] numChannels = BitConverter.GetBytes((short)channels);
        fileStream.Write(numChannels, 0, 2);

        byte[] sampleRate = BitConverter.GetBytes(hz);
        fileStream.Write(sampleRate, 0, 4);

        byte[] byteRate = BitConverter.GetBytes(hz * channels * 2);
        fileStream.Write(byteRate, 0, 4);

        byte[] blockAlign = BitConverter.GetBytes((short)(channels * 2));
        fileStream.Write(blockAlign, 0, 2);

        byte[] bitsPerSample = BitConverter.GetBytes((short)16);
        fileStream.Write(bitsPerSample, 0, 2);

        byte[] dataString = System.Text.Encoding.UTF8.GetBytes("data");
        fileStream.Write(dataString, 0, 4);

        byte[] subChunk2 = BitConverter.GetBytes(samples * channels * 2);
        fileStream.Write(subChunk2, 0, 4);
    }
}