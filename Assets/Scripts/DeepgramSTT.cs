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
    private const int TargetSampleRate = 16000;
    private const int TargetChannels = 1;
    private const int EndpointingMs = 1200;
    private const int UtteranceEndMs = 1800;
    private const float SilenceThreshold = 0.0125f;
    private const float SilenceFinalizeSeconds = 1.8f;
    private const float WebSocketFinalizeTimeoutSeconds = 12f;

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
        int bufferLengthSeconds = Mathf.CeilToInt(Mathf.Max(recordingDuration, 2f)) + 2;
        AudioClip clip = Microphone.Start(microphoneDevice, true, bufferLengthSeconds, TargetSampleRate);

        if (clip == null)
        {
            HasError = true;
            LastError = "Failed to start microphone recording.";
            Debug.LogError(LastError);
            yield break;
        }

        while (Microphone.GetPosition(microphoneDevice) <= 0)
        {
            yield return null;
        }

        var audioChunks = new ConcurrentQueue<byte[]>();
        var audioSignal = new SemaphoreSlim(0);
        bool audioFinished = false;
        CancellationTokenSource cts = new CancellationTokenSource();
        Task<string> transcribeTask;

        try
        {
            transcribeTask = TranscribeStreamAsync(audioChunks, audioSignal, () => audioFinished, cts.Token);

            int lastPosition = Microphone.GetPosition(microphoneDevice);
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
                        CaptureChunk(clip, lastPosition, currentPosition - lastPosition, audioChunks, audioSignal, ref speechDetected, ref lastVoiceTime);
                    }
                    else
                    {
                        CaptureChunk(clip, lastPosition, clip.samples - lastPosition, audioChunks, audioSignal, ref speechDetected, ref lastVoiceTime);
                        if (currentPosition > 0)
                        {
                            CaptureChunk(clip, 0, currentPosition, audioChunks, audioSignal, ref speechDetected, ref lastVoiceTime);
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
            cts.Dispose();
        }
    }

    private void CaptureChunk(AudioClip clip, int startSample, int sampleCount, ConcurrentQueue<byte[]> audioChunks, SemaphoreSlim audioSignal, ref bool speechDetected, ref float lastVoiceTime)
    {
        if (clip == null || sampleCount <= 0)
        {
            return;
        }

        float[] samples = new float[sampleCount * clip.channels];
        clip.GetData(samples, startSample);
        byte[] pcmBytes = AudioPcmUtility.ConvertFloatSamplesToPcm16(samples, sampleCount, clip.channels);

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
        ConcurrentQueue<byte[]> audioChunks,
        SemaphoreSlim audioSignal,
        System.Func<bool> isAudioFinished,
        CancellationToken cancellationToken)
    {
        string model = string.IsNullOrWhiteSpace(apiConfig.deepgramSTTModel) ? "nova-3-general" : apiConfig.deepgramSTTModel;
        string language = string.IsNullOrWhiteSpace(apiConfig.deepgramSTTLanguage) ? "de" : apiConfig.deepgramSTTLanguage;
        string url = BuildDeepgramUrl(model, language);

        using (ClientWebSocket websocket = new ClientWebSocket())
        {
            websocket.Options.KeepAliveInterval = TimeSpan.FromSeconds(10);
            websocket.Options.SetRequestHeader("Authorization", $"Token {apiConfig.deepgramApiKey}");

            await websocket.ConnectAsync(new Uri(url), cancellationToken).ConfigureAwait(false);

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
        string safeLanguage = string.IsNullOrWhiteSpace(language) ? "de" : language;
        return $"{DeepgramListenUrl}?model={UnityWebRequest.EscapeURL(safeModel)}&smart_format=true&encoding=linear16&sample_rate={TargetSampleRate}&channels={TargetChannels}&interim_results=true&endpointing={EndpointingMs}&utterance_end_ms={UtteranceEndMs}&vad_events=true&language={UnityWebRequest.EscapeURL(safeLanguage)}";
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
