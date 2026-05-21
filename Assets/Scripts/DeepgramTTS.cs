using System.Collections;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;

public class DeepgramTTS : MonoBehaviour
{
    private const string DeepgramSpeakUrl = "https://api.deepgram.com/v1/speak";
    private const int OutputSampleRate = 24000;

    [SerializeField] private ApiConfig apiConfig;
    [SerializeField] private AudioSource audioSource;

    public UnityEvent OnPlaybackComplete = new UnityEvent();

    public string LastError { get; private set; } = string.Empty;
    public bool HasError { get; private set; }

    private void Awake()
    {
        if (apiConfig == null)
        {
            apiConfig = Resources.Load<ApiConfig>("ApiConfig");
        }

        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
        }
    }

    public void Initialize(ApiConfig config, AudioSource source)
    {
        apiConfig = config != null ? config : apiConfig;
        audioSource = source != null ? source : audioSource;
    }

    public IEnumerator SpeakText(string text)
    {
        LastError = string.Empty;
        HasError = false;

        if (apiConfig == null)
        {
            HasError = true;
            LastError = "ApiConfig not assigned.";
            Debug.LogError(LastError);
            yield break;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            HasError = true;
            LastError = "Cannot synthesize empty text.";
            Debug.LogError(LastError);
            yield break;
        }

        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
        }

        if (audioSource == null)
        {
            HasError = true;
            LastError = "AudioSource component is missing.";
            Debug.LogError(LastError);
            yield break;
        }

        string url = BuildDeepgramUrl(apiConfig.deepgramTTSModel);
        var body = new JObject
        {
            ["text"] = text
        };

        byte[] payload = System.Text.Encoding.UTF8.GetBytes(body.ToString(Newtonsoft.Json.Formatting.None));

        using (UnityWebRequest request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
        {
            request.uploadHandler = new UploadHandlerRaw(payload);
            request.downloadHandler = new DownloadHandlerAudioClip(url, AudioType.WAV);
            request.uploadHandler.contentType = "application/json";
            request.SetRequestHeader("Authorization", $"Token {apiConfig.deepgramApiKey}");
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Accept", "audio/wav");

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                HasError = true;
                LastError = $"Deepgram TTS failed ({request.responseCode}): {request.error}";
                Debug.LogError(LastError);
                yield break;
            }

            AudioClip clip = DownloadHandlerAudioClip.GetContent(request);
            if (clip == null || clip.samples <= 0)
            {
                byte[] audioBytes = request.downloadHandler?.data;
                if (audioBytes != null && audioBytes.Length > 0)
                {
                    if (AudioPcmUtility.TryExtractPcm16FromWav(audioBytes, out byte[] pcmBytes))
                    {
                        clip = CreateClipFromPcm16(pcmBytes, OutputSampleRate);
                    }
                    else
                    {
                        clip = CreateClipFromPcm16(audioBytes, OutputSampleRate);
                    }
                }
            }

            if (clip == null)
            {
                HasError = true;
                string contentType = request.GetResponseHeader("Content-Type") ?? "unknown";
                LastError = $"Failed to build synthesized audio clip. Content-Type: {contentType}";
                Debug.LogError(LastError);
                yield break;
            }

            audioSource.Stop();
            audioSource.clip = clip;
            audioSource.loop = false;
            audioSource.Play();

            while (audioSource != null && audioSource.isPlaying)
            {
                yield return null;
            }

            OnPlaybackComplete?.Invoke();
        }
    }

    private static string BuildDeepgramUrl(string model)
    {
        string safeModel = string.IsNullOrWhiteSpace(model) ? "aura-asteria-en" : model;
        return $"{DeepgramSpeakUrl}?model={UnityWebRequest.EscapeURL(safeModel)}&encoding=linear16&sample_rate={OutputSampleRate}";
    }

    private static AudioClip CreateClipFromPcm16(byte[] pcmBytes, int sampleRate)
    {
        float[] samples = AudioPcmUtility.ConvertPcm16BytesToFloats(pcmBytes);
        if (samples.Length == 0)
        {
            return null;
        }

        AudioClip clip = AudioClip.Create("DeepgramTTS", samples.Length, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }
}
