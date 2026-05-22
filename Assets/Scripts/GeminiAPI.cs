using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;

[Serializable]
public class GeminiMessage
{
    public string role;
    public List<GeminiPart> parts = new List<GeminiPart>();
}

[Serializable]
public class GeminiPart
{
    public string text;
}

public class GeminiAPI : MonoBehaviour
{
    private const string GeminiStreamUrlTemplate = "https://generativelanguage.googleapis.com/v1beta/models/{0}:streamGenerateContent?alt=sse";

    [SerializeField] private ApiConfig apiConfig;
    [SerializeField] private List<GeminiMessage> conversationHistory = new List<GeminiMessage>();

    public UnityEvent<string> OnResponseReceived = new UnityEvent<string>();
    public UnityEvent<string> OnResponseDeltaReceived = new UnityEvent<string>();

    public string LastResponse { get; private set; } = string.Empty;
    public string LastError { get; private set; } = string.Empty;
    public bool HasError { get; private set; }
    public string ResponseLanguageCode { get; private set; } = ApiConfig.EnglishLanguageCode;

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

    public void SetResponseLanguage(string languageCode)
    {
        ResponseLanguageCode = ApiConfig.NormalizeLanguageCode(languageCode);
    }

    public void ClearConversation()
    {
        conversationHistory.Clear();
    }

    public IEnumerator SendChatMessage(string userMessage)
    {
        LastResponse = string.Empty;
        LastError = string.Empty;
        HasError = false;

        if (apiConfig == null)
        {
            HasError = true;
            LastError = "ApiConfig not assigned.";
            Debug.LogError(LastError);
            yield break;
        }

        if (string.IsNullOrWhiteSpace(userMessage))
        {
            HasError = true;
            LastError = "Cannot send an empty Gemini message.";
            Debug.LogError(LastError);
            yield break;
        }

        List<GeminiMessage> requestContents = new List<GeminiMessage>(conversationHistory);
        requestContents.Add(new GeminiMessage
        {
            role = "user",
            parts = new List<GeminiPart> { new GeminiPart { text = userMessage } }
        });

        string languageInstruction = ResponseLanguageCode == ApiConfig.GermanLanguageCode
            ? "Reply in German. Use German for all answers unless the user explicitly asks for another language."
            : "Reply in English. Use English for all answers unless the user explicitly asks for another language.";

        string systemPrompt = apiConfig != null && !string.IsNullOrWhiteSpace(apiConfig.geminiSystemPrompt)
            ? apiConfig.geminiSystemPrompt
            : "You are a helpful AI assistant. Always answer with a complete sentence or two. Never stop mid-sentence.";

        var body = new JObject
        {
            ["systemInstruction"] = new JObject
            {
                ["parts"] = new JArray
                {
                    new JObject { ["text"] = $"{systemPrompt}\n{languageInstruction}" }
                }
            },
            ["contents"] = JArray.FromObject(requestContents),
            ["generationConfig"] = new JObject
            {
                ["temperature"] = 0.4f,
                ["maxOutputTokens"] = 512
            }
        };

        string url = string.Format(GeminiStreamUrlTemplate, UnityWebRequest.EscapeURL(apiConfig.geminiModel))
            + $"&key={UnityWebRequest.EscapeURL(apiConfig.geminiApiKey)}";
        byte[] payload = System.Text.Encoding.UTF8.GetBytes(body.ToString(Formatting.None));

        using (UnityWebRequest request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
        {
            request.uploadHandler = new UploadHandlerRaw(payload);
            request.downloadHandler = new GeminiStreamDownloadHandler(HandleStreamChunk, HandleStreamError);
            request.uploadHandler.contentType = "application/json";
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Accept", "text/event-stream");

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                HasError = true;
                LastError = $"Gemini API failed ({request.responseCode}): {request.error}";
                Debug.LogError(LastError);
                yield break;
            }

            if (!string.IsNullOrWhiteSpace(LastError))
            {
                HasError = true;
                Debug.LogError(LastError);
                yield break;
            }
        }

        if (string.IsNullOrWhiteSpace(LastResponse))
        {
            HasError = true;
            LastError = "Gemini returned an empty response.";
            Debug.LogError(LastError);
            yield break;
        }

        conversationHistory.Add(new GeminiMessage
        {
            role = "user",
            parts = new List<GeminiPart> { new GeminiPart { text = userMessage } }
        });
        conversationHistory.Add(new GeminiMessage
        {
            role = "model",
            parts = new List<GeminiPart> { new GeminiPart { text = LastResponse } }
        });

        OnResponseReceived?.Invoke(LastResponse);
    }

    private void HandleStreamChunk(string delta)
    {
        if (string.IsNullOrEmpty(delta))
        {
            return;
        }

        LastResponse += delta;
        OnResponseDeltaReceived?.Invoke(delta);
    }

    private void HandleStreamError(string error)
    {
        if (!string.IsNullOrWhiteSpace(error))
        {
            LastError = $"Gemini stream error: {error}";
        }
    }

    private sealed class GeminiStreamDownloadHandler : DownloadHandlerScript
    {
        private readonly Action<string> onDelta;
        private readonly Action<string> onError;
        private readonly StringBuilder pendingText = new StringBuilder();

        public GeminiStreamDownloadHandler(Action<string> onDelta, Action<string> onError)
            : base(new byte[4096])
        {
            this.onDelta = onDelta;
            this.onError = onError;
        }

        protected override bool ReceiveData(byte[] data, int dataLength)
        {
            if (data == null || dataLength <= 0)
            {
                return true;
            }

            pendingText.Append(Encoding.UTF8.GetString(data, 0, dataLength));
            ProcessPendingEvents();
            return true;
        }

        protected override void CompleteContent()
        {
            ProcessPendingEvents(true);
        }

        private void ProcessPendingEvents(bool flush = false)
        {
            string text = pendingText.ToString().Replace("\r\n", "\n");
            int separatorIndex;

            while ((separatorIndex = text.IndexOf("\n\n", StringComparison.Ordinal)) >= 0)
            {
                string eventText = text.Substring(0, separatorIndex);
                ProcessEvent(eventText);
                text = text.Substring(separatorIndex + 2);
            }

            if (flush && !string.IsNullOrWhiteSpace(text))
            {
                ProcessEvent(text);
                text = string.Empty;
            }

            pendingText.Length = 0;
            pendingText.Append(text);
        }

        private void ProcessEvent(string eventText)
        {
            string[] lines = eventText.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string jsonText = line.Substring(5).Trim();
                if (string.IsNullOrWhiteSpace(jsonText) || jsonText == "[DONE]")
                {
                    continue;
                }

                try
                {
                    JObject json = JObject.Parse(jsonText);
                    string error = json["error"]?["message"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(error))
                    {
                        onError?.Invoke(error);
                        continue;
                    }

                    string delta = json["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.ToString();
                    if (!string.IsNullOrEmpty(delta))
                    {
                        onDelta?.Invoke(delta);
                    }
                }
                catch (Exception ex)
                {
                    onError?.Invoke(ex.Message);
                }
            }
        }
    }
}
