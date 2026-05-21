using UnityEngine;

[CreateAssetMenu(fileName = "ApiConfig", menuName = "MarineAI/ApiConfig")]
public class ApiConfig : ScriptableObject
{
    [Header("Deepgram")]
    public string deepgramApiKey;
    public string deepgramSTTModel = "nova-3-general";
    public string deepgramSTTLanguage = "de";
    public string deepgramTTSModel = "aura-2-lara-de";
    public string englishTTSModel = "aura-asteria-en";
    public string germanTTSModel = "aura-2-lara-de";

    [Header("Gemini")]
    public string geminiApiKey;
    public string geminiModel = "gemini-1.5-flash";
    [TextArea(3, 10)]
    public string geminiSystemPrompt = "You are Marina, an expert marine biology AI assistant.\nYou specialize in ocean ecosystems, marine species, coral reefs,\ndeep-sea biology, marine conservation, and oceanography.\nKeep answers concise (2-4 sentences) and engaging for a general audience.\nIf a question is not related to marine biology or oceans, politely redirect\nthe user back to marine topics.";

    public void ApplyLanguage(string languageCode)
    {
        string normalizedCode = string.IsNullOrWhiteSpace(languageCode) ? "de" : languageCode.Trim().ToLowerInvariant();
        deepgramSTTLanguage = normalizedCode;

        if (normalizedCode == "en")
        {
            deepgramTTSModel = englishTTSModel;
        }
        else if (normalizedCode == "de")
        {
            deepgramTTSModel = germanTTSModel;
        }
    }
}
