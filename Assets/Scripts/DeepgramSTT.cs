using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;

public class DeepgramSTT : MonoBehaviour
{
    private const string DeepgramListenUrl = "wss://api.deepgram.com/v1/listen";
    private const string DeepgramAuthTokenUrl = "https://api.deepgram.com/v1/auth/token";
    private const int TargetSampleRate = 16000;
    private const int TargetChannels = 1;
    private const int EndpointingMs = 1800;
    private const int UtteranceEndMs = 2200;
    private const float SilenceThreshold = 0.010f;
    private const float SilenceFinalizeSeconds = 2.3f;
    private const float WebSocketFinalizeTimeoutSeconds = 12f;
    private static readonly string[] DeepgramKeyterms =
    {
        "tell me something about starfish",
        "tell me something about sea stars",
        "tell me something about seahorses",
        "tell me something about",
        "tell me about",
        "something about",
        "starfish",
        "sea stars",
        "seahorses",
        "coral reefs",
        "jellyfish",
        "octopus",
        "cephalopods",
        "mollusks",
        "arthropods",
        "echinoderms",
        "cnidarians",
        "plankton",
        "bioluminescence",
        "crustaceans",
        "dolphins",
        "whales",
        "sharks",
        "marine biology",
        "ocean ecosystems"
    };

    [SerializeField] private ApiConfig apiConfig;

    public UnityEvent<string> OnTranscriptionComplete = new UnityEvent<string>();

    public string LastTranscript { get; private set; } = string.Empty;
    public string LastError { get; private set; } = string.Empty;
    public bool HasError { get; private set; }

    private string microphoneDevice;

    private void Awake()
    {
        if (apiConfig == null)
        {
            apiConfig = Resources.Load<ApiConfig>("ApiConfig");
        }
    }

    public void Initialize(ApiConfig config)
    {
        apiConfig = config != null ? config : apiConfig;
    }

    public IEnumerator RecordAndTranscribe(float recordingDuration = 5f)
    {
        LastTranscript = string.Empty;
        LastError = string.Empty;
        HasError = false;

        if (apiConfig == null)
        {
            HasError = true;
            LastError = "ApiConfig not assigned.";
            Debug.LogError(LastError);
            yield break;
        }

        string apiKey = apiConfig.deepgramApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            HasError = true;
            LastError = "Deepgram API key is missing. Set DEEPGRAM_API_KEY in .env or ApiConfig.";
            Debug.LogError(LastError);
            yield break;
        }

        using (UnityWebRequest authRequest = UnityWebRequest.Get(DeepgramAuthTokenUrl))
        {
            authRequest.SetRequestHeader("Authorization", $"Token {apiKey}");
            yield return authRequest.SendWebRequest();

            if (authRequest.result != UnityWebRequest.Result.Success)
            {
                HasError = true;
                LastError = authRequest.responseCode == 401
                    ? "Deepgram API key was rejected. Create a new Deepgram API key in the Deepgram Console and update DEEPGRAM_API_KEY."
                    : $"Deepgram auth check failed ({authRequest.responseCode}): {authRequest.error}";
                Debug.LogError(LastError);
                yield break;
            }
        }

        if (Microphone.devices == null || Microphone.devices.Length == 0)
        {
            HasError = true;
            LastError = "No microphone device found.";
            Debug.LogError(LastError);
            yield break;
        }

        if (!Application.HasUserAuthorization(UserAuthorization.Microphone))
        {
            yield return Application.RequestUserAuthorization(UserAuthorization.Microphone);
            if (!Application.HasUserAuthorization(UserAuthorization.Microphone))
            {
                HasError = true;
                LastError = "Microphone permission was not granted.";
                Debug.LogError(LastError);
                yield break;
            }
        }

        microphoneDevice = Microphone.devices.First();
        int microphoneSampleRate = GetMicrophoneSampleRate(microphoneDevice);
        int bufferLengthSeconds = Mathf.CeilToInt(Mathf.Max(recordingDuration, 2f)) + 2;
        var audioChunks = new ConcurrentQueue<byte[]>();
        var audioSignal = new SemaphoreSlim(0);
        bool audioFinished = false;
        CancellationTokenSource cts = new CancellationTokenSource();
        Task<string> transcribeTask;
        AudioClip clip = null;

        try
        {
            transcribeTask = TranscribeStreamAsync(apiKey, audioChunks, audioSignal, () => audioFinished, cts.Token);

            clip = Microphone.Start(microphoneDevice, true, bufferLengthSeconds, microphoneSampleRate);

            if (clip == null)
            {
                cts.Cancel();
                HasError = true;
                LastError = "Failed to start microphone recording.";
                Debug.LogError(LastError);
                yield break;
            }

            while (Microphone.GetPosition(microphoneDevice) <= 0)
            {
                yield return null;
            }

            int lastPosition = 0;
            float startTime = Time.unscaledTime;
            float lastVoiceTime = startTime;
            bool speechDetected = false;

            while (!transcribeTask.IsCompleted && Time.unscaledTime - startTime < recordingDuration)
            {
                int currentPosition = Microphone.GetPosition(microphoneDevice);
                if (currentPosition < 0)
                {
                    break;
                }

                if (currentPosition != lastPosition)
                {
                    if (currentPosition > lastPosition)
                    {
                        CaptureChunk(clip, lastPosition, currentPosition - lastPosition, microphoneSampleRate, audioChunks, audioSignal, ref speechDetected, ref lastVoiceTime);
                    }
                    else
                    {
                        CaptureChunk(clip, lastPosition, clip.samples - lastPosition, microphoneSampleRate, audioChunks, audioSignal, ref speechDetected, ref lastVoiceTime);
                        if (currentPosition > 0)
                        {
                            CaptureChunk(clip, 0, currentPosition, microphoneSampleRate, audioChunks, audioSignal, ref speechDetected, ref lastVoiceTime);
                        }
                    }

                    lastPosition = currentPosition;
                }

                if (speechDetected && Time.unscaledTime - lastVoiceTime >= SilenceFinalizeSeconds)
                {
                    break;
                }

                yield return null;
            }

            Microphone.End(microphoneDevice);
            clip = null;
            audioFinished = true;
            audioSignal.Release();

            if (!transcribeTask.IsCompleted)
            {
                float waitStart = Time.unscaledTime;
                while (!transcribeTask.IsCompleted && Time.unscaledTime - waitStart < WebSocketFinalizeTimeoutSeconds)
                {
                    yield return null;
                }
            }

            if (!transcribeTask.IsCompleted)
            {
                cts.Cancel();
                HasError = true;
                LastError = "Deepgram STT websocket timed out.";
                Debug.LogError(LastError);
                yield break;
            }

            if (transcribeTask.IsFaulted)
            {
                cts.Cancel();
                HasError = true;
                LastError = $"Deepgram STT websocket failed: {transcribeTask.Exception?.GetBaseException().Message}";
                Debug.LogError(LastError);
                yield break;
            }

            LastTranscript = transcribeTask.Result?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(LastTranscript))
            {
                Debug.LogWarning("Deepgram STT websocket returned an empty transcript.");
            }

            OnTranscriptionComplete?.Invoke(LastTranscript);
        }
        finally
        {
            if (clip != null)
            {
                Microphone.End(microphoneDevice);
            }

            cts.Dispose();
        }
    }

    private void CaptureChunk(AudioClip clip, int startSample, int sampleCount, int sourceSampleRate, ConcurrentQueue<byte[]> audioChunks, SemaphoreSlim audioSignal, ref bool speechDetected, ref float lastVoiceTime)
    {
        if (clip == null || sampleCount <= 0)
        {
            return;
        }

        float[] samples = new float[sampleCount * clip.channels];
        clip.GetData(samples, startSample);
        byte[] pcmBytes = AudioPcmUtility.ConvertFloatSamplesToPcm16(samples, sampleCount, clip.channels, sourceSampleRate, TargetSampleRate);

        if (pcmBytes.Length > 0)
        {
            audioChunks.Enqueue(pcmBytes);
            audioSignal.Release();
        }

        float maxAbs = 0f;
        for (int i = 0; i < samples.Length; i++)
        {
            float abs = Mathf.Abs(samples[i]);
            if (abs > maxAbs)
            {
                maxAbs = abs;
            }
        }

        if (maxAbs >= SilenceThreshold)
        {
            speechDetected = true;
            lastVoiceTime = Time.unscaledTime;
        }
    }

    private async Task<string> TranscribeStreamAsync(
        string apiKey,
        ConcurrentQueue<byte[]> audioChunks,
        SemaphoreSlim audioSignal,
        System.Func<bool> isAudioFinished,
        CancellationToken cancellationToken)
    {
        string model = string.IsNullOrWhiteSpace(apiConfig.deepgramSTTModel) ? "nova-3-general" : apiConfig.deepgramSTTModel;
        string language = ApiConfig.NormalizeLanguageCode(apiConfig.deepgramSTTLanguage);
        string url = BuildDeepgramUrl(model, language);

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("Deepgram API key is missing.");
        }

        using (ClientWebSocket websocket = new ClientWebSocket())
        {
            websocket.Options.KeepAliveInterval = TimeSpan.FromSeconds(10);
            websocket.Options.SetRequestHeader("Authorization", $"Token {apiKey}");

            try
            {
                await websocket.ConnectAsync(new Uri(url), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed to connect to Deepgram websocket for model '{model}' and language '{language}': {ex.Message}",
                    ex);
            }

            var transcriptTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationTokenSource innerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            Task sendTask = SendAudioChunksAsync(websocket, audioChunks, audioSignal, isAudioFinished, innerCts.Token);
            Task receiveTask = ReceiveTranscriptionAsync(websocket, transcriptTcs, innerCts.Token);

            string transcript = string.Empty;

            try
            {
                Task completed = await Task.WhenAny(transcriptTcs.Task, Task.Delay(TimeSpan.FromSeconds(WebSocketFinalizeTimeoutSeconds), cancellationToken)).ConfigureAwait(false);
                if (completed == transcriptTcs.Task)
                {
                    transcript = await transcriptTcs.Task.ConfigureAwait(false);
                    innerCts.Cancel();
                }
                else
                {
                    throw new TimeoutException("Deepgram STT websocket did not finalize in time.");
                }
            }
            finally
            {
                if (!innerCts.IsCancellationRequested)
                {
                    innerCts.Cancel();
                }

                try
                {
                    await Task.WhenAll(sendTask, receiveTask).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    if (!transcriptTcs.Task.IsCompleted)
                    {
                        transcriptTcs.TrySetException(ex);
                    }
                }

                if (websocket.State == WebSocketState.Open || websocket.State == WebSocketState.CloseReceived)
                {
                    try
                    {
                        await websocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Completed", CancellationToken.None).ConfigureAwait(false);
                    }
                    catch
                    {
                    }
                }

                innerCts.Dispose();
            }

            return transcript;
        }
    }

    private async Task SendAudioChunksAsync(
        ClientWebSocket websocket,
        ConcurrentQueue<byte[]> audioChunks,
        SemaphoreSlim audioSignal,
        System.Func<bool> isAudioFinished,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (audioChunks.TryDequeue(out byte[] chunk))
                {
                    if (chunk.Length > 0)
                    {
                        await websocket.SendAsync(new ArraySegment<byte>(chunk), WebSocketMessageType.Binary, true, cancellationToken).ConfigureAwait(false);
                    }

                    continue;
                }

                if (isAudioFinished())
                {
                    break;
                }

                if (audioSignal.CurrentCount == 0)
                {
                    await audioSignal.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
            }

            string finalizeMessage = "{\"type\":\"Finalize\"}";
            byte[] finalizeBytes = Encoding.UTF8.GetBytes(finalizeMessage);
            await websocket.SendAsync(new ArraySegment<byte>(finalizeBytes), WebSocketMessageType.Text, true, CancellationToken.None).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task ReceiveTranscriptionAsync(
        ClientWebSocket websocket,
        TaskCompletionSource<string> transcriptTcs,
        CancellationToken cancellationToken)
    {
        string accumulatedFinalTranscript = string.Empty;

        try
        {
            while (!cancellationToken.IsCancellationRequested && websocket.State == WebSocketState.Open)
            {
                string message = await ReceiveTextMessageAsync(websocket, cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(message))
                {
                    continue;
                }

                JObject json = JObject.Parse(message);
                string type = json["type"]?.ToString();

                if (!string.Equals(type, "Results", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string transcript = json["channel"]?["alternatives"]?[0]?["transcript"]?.ToString()?.Trim() ?? string.Empty;
                bool isFinal = json["is_final"]?.Value<bool>() ?? false;
                bool fromFinalize = json["from_finalize"]?.Value<bool>() ?? false;

                if (isFinal && !string.IsNullOrWhiteSpace(transcript))
                {
                    accumulatedFinalTranscript = MergeTranscript(accumulatedFinalTranscript, transcript);
                }

                if (fromFinalize)
                {
                    string finalTranscript = !string.IsNullOrWhiteSpace(transcript)
                        ? MergeTranscript(accumulatedFinalTranscript, transcript)
                        : accumulatedFinalTranscript;

                    transcriptTcs.TrySetResult(finalTranscript.Trim());
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            transcriptTcs.TrySetException(ex);
            return;
        }

        if (!transcriptTcs.Task.IsCompleted && !string.IsNullOrWhiteSpace(accumulatedFinalTranscript))
        {
            transcriptTcs.TrySetResult(accumulatedFinalTranscript.Trim());
        }
    }

    private static async Task<string> ReceiveTextMessageAsync(ClientWebSocket websocket, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[4096];
        System.IO.MemoryStream memoryStream = new System.IO.MemoryStream();

        try
        {
            while (true)
            {
                WebSocketReceiveResult result = await websocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken).ConfigureAwait(false);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return string.Empty;
                }

                memoryStream.Write(buffer, 0, result.Count);

                if (result.EndOfMessage)
                {
                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        return Encoding.UTF8.GetString(memoryStream.ToArray());
                    }

                    memoryStream.SetLength(0);
                }
            }
        }
        finally
        {
            memoryStream.Dispose();
        }
    }

    private static string BuildDeepgramUrl(string model, string language)
    {
        string safeModel = string.IsNullOrWhiteSpace(model) ? "nova-3-general" : model;
        string safeLanguage = ApiConfig.NormalizeLanguageCode(language);
        StringBuilder urlBuilder = new StringBuilder();
        urlBuilder.Append($"{DeepgramListenUrl}?model={UnityWebRequest.EscapeURL(safeModel)}");
        urlBuilder.Append($"&language={UnityWebRequest.EscapeURL(safeLanguage)}");
        urlBuilder.Append("&smart_format=true");
        urlBuilder.Append("&punctuate=true");
        urlBuilder.Append("&utterances=true");
        urlBuilder.Append($"&encoding=linear16&sample_rate={TargetSampleRate}&channels={TargetChannels}");
        urlBuilder.Append($"&interim_results=true&endpointing={EndpointingMs}&utterance_end_ms={UtteranceEndMs}&vad_events=true");

        for (int i = 0; i < DeepgramKeyterms.Length; i++)
        {
            urlBuilder.Append("&keyterm=");
            urlBuilder.Append(UnityWebRequest.EscapeURL(DeepgramKeyterms[i]));
        }

        return urlBuilder.ToString();
    }

    private static int GetMicrophoneSampleRate(string deviceName)
    {
        Microphone.GetDeviceCaps(deviceName, out int minFrequency, out int maxFrequency);

        if (maxFrequency > 0)
        {
            return maxFrequency;
        }

        if (minFrequency > 0)
        {
            return minFrequency;
        }

        return 48000;
    }

    private static string MergeTranscript(string existing, string incoming)
    {
        string left = existing?.Trim() ?? string.Empty;
        string right = incoming?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(left))
        {
            return right;
        }

        if (string.IsNullOrWhiteSpace(right))
        {
            return left;
        }

        if (left.IndexOf(right, StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return left;
        }

        if (right.IndexOf(left, StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return right;
        }

        return $"{left} {right}".Trim();
    }

}
