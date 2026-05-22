using UnityEngine;

[CreateAssetMenu(fileName = "ApiConfig", menuName = "MarineAI/ApiConfig")]
public class ApiConfig : ScriptableObject
{
    public const string EnglishLanguageCode = "en";
    public const string GermanLanguageCode = "de";

    [Header("Deepgram")]
    [Tooltip("Fallback/Override API Key if not specified in .env file or Environment Variables")]
    [SerializeField] private string deepgramApiKeyFallback;
    public string deepgramApiKey
    {
        get
        {
            LoadEnv();
            return !string.IsNullOrEmpty(_envDeepgramApiKey) ? _envDeepgramApiKey : deepgramApiKeyFallback;
        }
        set { deepgramApiKeyFallback = value; }
    }
    public string deepgramSTTModel = "nova-3-general";
    public string deepgramSTTLanguage = "en";
    public string deepgramTTSModel = "aura-asteria-en";
    public string englishTTSModel = "aura-asteria-en";
    public string germanTTSModel = "aura-2-lara-de";

    [Header("Gemini")]
    [Tooltip("Fallback/Override API Key if not specified in .env file or Environment Variables")]
    [SerializeField] private string geminiApiKeyFallback;
    public string geminiApiKey
    {
        get
        {
            LoadEnv();
            return !string.IsNullOrEmpty(_envGeminiApiKey) ? _envGeminiApiKey : geminiApiKeyFallback;
        }
        set { geminiApiKeyFallback = value; }
    }
    public string geminiModel = "gemini-1.5-flash";
    [TextArea(3, 10)]
    public string geminiSystemPrompt = "You are Marina, an expert marine biology AI assistant.\nYou specialize in ocean ecosystems, marine species, coral reefs,\ndeep-sea biology, marine conservation, and oceanography.\nKeep answers concise (2-4 sentences) and engaging for a general audience.\nIf a question is not related to marine biology or oceans, politely redirect\nthe user back to marine topics.";

    private static bool _envLoaded = false;
    private static string _envDeepgramApiKey;
    private static string _envGeminiApiKey;

    private static void LoadEnv()
    {
        if (_envLoaded) return;

        // 1. Try to read from environment variables first
        _envDeepgramApiKey = System.Environment.GetEnvironmentVariable("DEEPGRAM_API_KEY");
        _envGeminiApiKey = System.Environment.GetEnvironmentVariable("GEMINI_API_KEY");

        // 2. If not found, try to read from local .env file
        if (string.IsNullOrEmpty(_envDeepgramApiKey) || string.IsNullOrEmpty(_envGeminiApiKey))
        {
            string envPath = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), ".env");
            if (!System.IO.File.Exists(envPath))
            {
                envPath = System.IO.Path.Combine(UnityEngine.Application.dataPath, "..", ".env");
            }

            if (System.IO.File.Exists(envPath))
            {
                try
                {
                    string[] lines = System.IO.File.ReadAllLines(envPath);
                    foreach (string line in lines)
                    {
                        if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#"))
                            continue;

                        int index = line.IndexOf('=');
                        if (index > 0)
                        {
                            string key = line.Substring(0, index).Trim();
                            string value = line.Substring(index + 1).Trim();

                            // Strip quotes
                            if (value.StartsWith("\"") && value.EndsWith("\"") && value.Length >= 2)
                            {
                                value = value.Substring(1, value.Length - 2);
                            }
                            else if (value.StartsWith("'") && value.EndsWith("'") && value.Length >= 2)
                            {
                                value = value.Substring(1, value.Length - 2);
                            }

                            if (key == "DEEPGRAM_API_KEY" && string.IsNullOrEmpty(_envDeepgramApiKey))
                            {
                                _envDeepgramApiKey = value;
                            }
                            else if (key == "GEMINI_API_KEY" && string.IsNullOrEmpty(_envGeminiApiKey))
                            {
                                _envGeminiApiKey = value;
                            }
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    UnityEngine.Debug.LogError($"Error reading .env file: {ex.Message}");
                }
            }
        }

        _envLoaded = true;
    }

    public void ApplyLanguage(string languageCode)
    {
        string normalizedCode = NormalizeLanguageCode(languageCode);
        deepgramSTTLanguage = normalizedCode;

        if (normalizedCode == EnglishLanguageCode)
        {
            deepgramTTSModel = englishTTSModel;
        }
        else if (normalizedCode == GermanLanguageCode)
        {
            deepgramTTSModel = germanTTSModel;
        }
    }

    public static string NormalizeLanguageCode(string languageCode)
    {
        string normalizedCode = string.IsNullOrWhiteSpace(languageCode)
            ? EnglishLanguageCode
            : languageCode.Trim().ToLowerInvariant();

        return normalizedCode == GermanLanguageCode ? GermanLanguageCode : EnglishLanguageCode;
    }
}

