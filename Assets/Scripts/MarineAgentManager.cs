using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Events;

public enum AgentState
{
    Idle,
    Listening,
    Thinking,
    Speaking
}

public class MarineAgentManager : MonoBehaviour
{
    [SerializeField] private ApiConfig apiConfig;
    [SerializeField] private DeepgramSTT stt;
    [SerializeField] private GeminiAPI gemini;
    [SerializeField] private DeepgramTTS tts;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private UIManager uiManager;

    public UnityEvent<AgentState> OnStateChanged = new UnityEvent<AgentState>();
    public UnityEvent<string> OnTranscriptChanged = new UnityEvent<string>();
    public UnityEvent<string> OnResponseChanged = new UnityEvent<string>();
    public UnityEvent<string> OnError = new UnityEvent<string>();

    public AgentState CurrentState { get; private set; } = AgentState.Idle;
    public string LastTranscript { get; private set; } = string.Empty;
    public string LastResponse { get; private set; } = string.Empty;
    public string CurrentLanguageCode => apiConfig != null ? apiConfig.deepgramSTTLanguage : "de";

    private bool isRunning;
    private float lastPipelineStartTime = -999f;
    private const float PipelineCooldownSeconds = 0.5f;
    private bool speechWorkerRunning;

    private void Awake()
    {
        if (apiConfig == null)
        {
            apiConfig = Resources.Load<ApiConfig>("ApiConfig");
        }

        if (stt == null)
        {
            stt = GetComponent<DeepgramSTT>();
        }

        if (gemini == null)
        {
            gemini = GetComponent<GeminiAPI>();
        }

        if (tts == null)
        {
            tts = GetComponent<DeepgramTTS>();
        }

        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
        }

        if (uiManager == null)
        {
            uiManager = FindFirstObjectByType<UIManager>();
        }

        if (stt != null)
        {
            stt.OnTranscriptionComplete.AddListener(HandleTranscriptionComplete);
        }

        if (gemini != null)
        {
            gemini.OnResponseReceived.AddListener(HandleGeminiResponse);
            gemini.OnResponseDeltaReceived.AddListener(HandleGeminiResponseDelta);
        }

        if (tts != null)
        {
            tts.OnPlaybackComplete.AddListener(HandlePlaybackComplete);
        }

        if (stt != null)
        {
            stt.Initialize(apiConfig);
        }

        if (gemini != null)
        {
            gemini.Initialize(apiConfig);
            gemini.SetResponseLanguage(CurrentLanguageCode);
        }

        if (tts != null)
        {
            tts.Initialize(apiConfig, audioSource);
        }
    }

    private void Start()
    {
        if (uiManager == null)
        {
            uiManager = FindFirstObjectByType<UIManager>();
        }

        if (uiManager != null)
        {
            uiManager.BindManager(this);
        }

        SetState(AgentState.Idle);
    }

    public void SetLanguage(string languageCode)
    {
        if (isRunning)
        {
            return;
        }

        if (apiConfig == null)
        {
            apiConfig = Resources.Load<ApiConfig>("ApiConfig");
        }

        if (apiConfig == null)
        {
            ReportError("ApiConfig not assigned.");
            return;
        }

        apiConfig.ApplyLanguage(languageCode);
        stt?.Initialize(apiConfig);
        gemini?.Initialize(apiConfig);
        gemini?.SetResponseLanguage(apiConfig.deepgramSTTLanguage);
        tts?.Initialize(apiConfig, audioSource);
        gemini?.ClearConversation();
    }

    public void StartPipeline()
    {
        if (isRunning)
        {
            return;
        }

        if (Time.unscaledTime - lastPipelineStartTime < PipelineCooldownSeconds)
        {
            return;
        }

        StartCoroutine(RunPipeline());
    }

    public IEnumerator RunPipeline()
    {
        if (isRunning)
        {
            yield break;
        }

        isRunning = true;
        lastPipelineStartTime = Time.unscaledTime;
        LastTranscript = string.Empty;
        LastResponse = string.Empty;
        speechWorkerRunning = false;

        SetState(AgentState.Listening);
        yield return StartCoroutine(stt.RecordAndTranscribe(8f));

        if (stt == null || stt.HasError)
        {
            ReportError(stt != null ? stt.LastError : "STT component missing.");
            SetState(AgentState.Idle);
            isRunning = false;
            yield break;
        }

        if (string.IsNullOrWhiteSpace(LastTranscript))
        {
            uiManager?.ShowTranscriptHint("Didn't catch that, try again");
            SetState(AgentState.Idle);
            isRunning = false;
            yield break;
        }

        SetState(AgentState.Thinking);
        yield return StartCoroutine(gemini.SendChatMessage(LastTranscript));

        if (gemini == null || gemini.HasError)
        {
            string errorText = gemini != null ? gemini.LastError : "Gemini component missing.";
            uiManager?.ShowGeminiError(errorText);
            ReportError(errorText);
            SetState(AgentState.Idle);
            isRunning = false;
            yield break;
        }

        if (string.IsNullOrWhiteSpace(LastResponse))
        {
            ReportError("Gemini returned an empty response.");
            SetState(AgentState.Idle);
            isRunning = false;
            yield break;
        }

        SetState(AgentState.Speaking);
        yield return StartCoroutine(tts.SpeakText(LastResponse));

        if (tts != null && tts.HasError)
        {
            uiManager?.ShowTtsFallback(LastResponse);
        }

        SetState(AgentState.Idle);
        isRunning = false;
    }

    private void HandleTranscriptionComplete(string transcript)
    {
        LastTranscript = transcript;
        OnTranscriptChanged?.Invoke(LastTranscript);
        uiManager?.SetTranscriptText(LastTranscript);
    }

    private void HandleGeminiResponse(string response)
    {
        LastResponse = response;
        OnResponseChanged?.Invoke(LastResponse);
        uiManager?.SetResponseText(LastResponse);
    }

    private void HandleGeminiResponseDelta(string delta)
    {
        if (string.IsNullOrWhiteSpace(delta))
        {
            return;
        }

        LastResponse += delta;
        OnResponseChanged?.Invoke(LastResponse);
        uiManager?.SetResponseText(LastResponse);
    }

    private void HandlePlaybackComplete()
    {
        uiManager?.ClearError();
    }

    private void SetState(AgentState state)
    {
        CurrentState = state;
        OnStateChanged?.Invoke(state);
        uiManager?.SetState(state);
    }

    private void ReportError(string error)
    {
        Debug.LogError(error);
        OnError?.Invoke(error);
        uiManager?.ShowGeminiError(error);
    }

}
