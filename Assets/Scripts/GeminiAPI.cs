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
    public string ResponseLanguageCode { get; private set; } = "de";

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
        string normalizedCode = string.IsNullOrWhiteSpace(languageCode) ? "de" : languageCode.Trim().ToLowerInvariant();
        ResponseLanguageCode = normalizedCode;
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

        string languageInstruction = ResponseLanguageCode == "de"
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

        string url = $"https://generativelanguage.googleapis.com/v1beta/models/{UnityWebRequest.EscapeURL(apiConfig.geminiModel)}:generateContent?key={UnityWebRequest.EscapeURL(apiConfig.geminiApiKey)}";
        byte[] payload = System.Text.Encoding.UTF8.GetBytes(body.ToString(Formatting.None));

        using (UnityWebRequest request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
        {
            request.uploadHandler = new UploadHandlerRaw(payload);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.uploadHandler.contentType = "application/json";
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                HasError = true;
                LastError = $"Gemini API failed ({request.responseCode}): {request.error}";
                Debug.LogError(LastError);
                yield break;
            }

            try
            {
                JObject json = JObject.Parse(request.downloadHandler.text);
                string responseText = json["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.ToString();
                LastResponse = responseText?.Trim() ?? string.Empty;
            }
            catch (Exception ex)
            {
                HasError = true;
                LastError = $"Gemini response parse error: {ex.Message}";
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
}
