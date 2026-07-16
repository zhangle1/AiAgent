using AiAgent.Backend.Entities.Settings;

namespace AiAgent.Backend.Services.Settings;

/// <summary>
/// 模型、搜索、语音和多媒体服务供应商的默认种子数据。
/// </summary>
public static class ModelProviderSeedData
{
    /// <summary>
    /// 系统启动时用于初始化数据库的内置供应商列表。
    /// </summary>
    public static IReadOnlyList<AiModelProvider> Providers { get; } =
    [
        Llm("aihubmix", "AiHubMix", "https://aihubmix.com/v1", null, 10),
        Llm("anthropic", "Anthropic", "https://api.anthropic.com", "claude-sonnet-4-20250514", 20, "anthropic", "x-api-key"),
        Llm("azure_openai", "Azure OpenAI", null, null, 30, "azure_openai", "api-key"),
        Llm("byteplus", "BytePlus", "https://ark.ap-southeast.bytepluses.com/api/v3", null, 40),
        Llm("byteplus_coding", "BytePlus Coding Plan", "https://ark.ap-southeast.bytepluses.com/api/v3", null, 50),
        Llm("custom_anthropic", "Custom (Anthropic API)", null, null, 60, "anthropic", "x-api-key"),
        Llm("custom_openai", "Custom (OpenAI API)", null, null, 70),
        Llm("dashscope", "DashScope", "https://dashscope.aliyuncs.com/compatible-mode/v1", "qwen-plus", 80),
        Llm("deepseek", "DeepSeek", "https://api.deepseek.com", "deepseek-chat", 90),
        Llm("gemini", "Gemini", "https://generativelanguage.googleapis.com/v1beta/openai", "gemini-2.5-flash", 100),
        Llm("github_copilot", "GitHub Copilot", null, null, 110, "github_copilot", "oauth"),
        Llm("groq", "Groq", "https://api.groq.com/openai/v1", null, 120),
        Llm("lemonade", "Lemonade", "http://localhost:8000/api/v1", null, 130),
        Llm("llama_cpp", "llama.cpp", "http://localhost:8080/v1", null, 140),
        Llm("lm_studio", "LM Studio", "http://localhost:1234/v1", null, 150),
        Llm("minimax", "MiniMax", "https://api.minimax.io/v1", null, 160),
        Llm("minimax_anthropic", "MiniMax (Anthropic)", "https://api.minimax.io/anthropic", null, 170, "anthropic", "x-api-key"),
        Llm("mistral", "Mistral", "https://api.mistral.ai/v1", null, 180),
        Llm("moonshot", "Moonshot", "https://api.moonshot.cn/v1", null, 190),
        Llm("ollama", "Ollama", "http://localhost:11434/v1", null, 200),
        Llm("openai", "OpenAI", "https://api.openai.com/v1", "gpt-4.1", 210, "openai"),
        Llm("openrouter", "OpenRouter", "https://openrouter.ai/api/v1", null, 220),
        Llm("siliconflow", "SiliconFlow", "https://api.siliconflow.cn/v1", null, 230),
        Llm("zhipu", "Zhipu AI", "https://open.bigmodel.cn/api/paas/v4", null, 240),

        Embedding("openai", "OpenAI", "https://api.openai.com/v1", "text-embedding-3-small", 1536, 10),
        Embedding("openai_compatible", "Custom (OpenAI API)", null, null, null, 20),
        Embedding("dashscope", "DashScope", "https://dashscope.aliyuncs.com/compatible-mode/v1", "text-embedding-v4", 1024, 30),
        Embedding("jina", "Jina", "https://api.jina.ai/v1", "jina-embeddings-v3", 1024, 40),
        Embedding("ollama", "Ollama", "http://localhost:11434", null, null, 50),
        Embedding("cohere", "Cohere", "https://api.cohere.com/v2", null, null, 60),
        Embedding("siliconflow", "SiliconFlow", "https://api.siliconflow.cn/v1", null, null, 70),
        Embedding("gemini", "Gemini", "https://generativelanguage.googleapis.com/v1beta/openai", null, null, 80),
        Embedding("openrouter", "OpenRouter", "https://openrouter.ai/api/v1", null, null, 90),
        Embedding("lm_studio", "LM Studio", "http://localhost:1234/v1", null, null, 100),

        Provider("search", "tavily", "Tavily", "tavily_search", "https://api.tavily.com", "bearer", 10),
        Provider("search", "brave", "Brave Search", "brave_search", "https://api.search.brave.com", "x-subscription-token", 20),
        Provider("search", "serper", "Serper", "serper_search", "https://google.serper.dev", "x-api-key", 30),
        Provider("search", "searxng", "SearXNG", "searxng_search", null, "none", 40),
        Provider("search", "jina", "Jina", "jina_search", "https://s.jina.ai", "bearer", 50),
        Provider("search", "duckduckgo", "DuckDuckGo", "duckduckgo_search", null, "none", 60),
        Provider("search", "exa", "Exa", "exa_search", "https://api.exa.ai", "x-api-key", 70),
        Provider("search", "perplexity", "Perplexity", "perplexity_search", "https://api.perplexity.ai", "bearer", 80),
        Provider("search", "openrouter", "OpenRouter", "openrouter_search", "https://openrouter.ai/api/v1", "bearer", 90),
        Provider("search", "baidu", "Baidu", "baidu_search", null, "bearer", 100),

        Provider("tts", "openai", "OpenAI", "openai_tts", "https://api.openai.com/v1", "bearer", 10, "gpt-4o-mini-tts", null, "alloy"),
        Provider("tts", "azure_openai", "Azure OpenAI", "azure_tts", null, "api-key", 20),
        Provider("tts", "groq", "Groq", "openai_tts", "https://api.groq.com/openai/v1", "bearer", 30),
        Provider("tts", "openrouter", "OpenRouter", "openrouter_tts", "https://openrouter.ai/api/v1", "bearer", 40),
        Provider("tts", "siliconflow", "SiliconFlow", "openai_tts", "https://api.siliconflow.cn/v1", "bearer", 50),
        Provider("tts", "elevenlabs", "ElevenLabs", "elevenlabs_tts", "https://api.elevenlabs.io/v1", "xi-api-key", 60),
        Provider("tts", "gemini", "Gemini", "gemini_tts", "https://generativelanguage.googleapis.com/v1beta", "bearer", 70),

        Provider("stt", "openai", "OpenAI", "openai_stt", "https://api.openai.com/v1", "bearer", 10, "gpt-4o-mini-transcribe"),
        Provider("stt", "azure_openai", "Azure OpenAI", "azure_stt", null, "api-key", 20),
        Provider("stt", "groq", "Groq", "openai_stt", "https://api.groq.com/openai/v1", "bearer", 30),
        Provider("stt", "openrouter", "OpenRouter", "openrouter_stt", "https://openrouter.ai/api/v1", "bearer", 40),
        Provider("stt", "siliconflow", "SiliconFlow", "openai_stt", "https://api.siliconflow.cn/v1", "bearer", 50),
        Provider("stt", "deepgram", "Deepgram", "deepgram_stt", "https://api.deepgram.com/v1", "token", 60),
        Provider("stt", "gemini", "Gemini", "gemini_stt", "https://generativelanguage.googleapis.com/v1beta", "bearer", 70),

        Provider("imagegen", "openai", "OpenAI", "openai_image", "https://api.openai.com/v1", "bearer", 10, "gpt-image-1"),
        Provider("imagegen", "azure_openai", "Azure OpenAI", "azure_image", null, "api-key", 20),
        Provider("imagegen", "openrouter", "OpenRouter", "openrouter_image", "https://openrouter.ai/api/v1", "bearer", 30),
        Provider("imagegen", "siliconflow", "SiliconFlow", "openai_image", "https://api.siliconflow.cn/v1", "bearer", 40),
        Provider("imagegen", "gemini", "Gemini", "gemini_image", "https://generativelanguage.googleapis.com/v1beta", "bearer", 50),
        Provider("imagegen", "dashscope", "DashScope", "dashscope_image", "https://dashscope.aliyuncs.com/api/v1", "bearer", 60),

        Provider("videogen", "dashscope", "DashScope", "dashscope_video", "https://dashscope.aliyuncs.com/api/v1", "bearer", 10),
        Provider("videogen", "minimax", "MiniMax", "minimax_video", "https://api.minimax.io/v1", "bearer", 20),
        Provider("videogen", "kling", "Kling", "kling_video", null, "bearer", 30),
        Provider("videogen", "runway", "Runway", "runway_video", "https://api.runwayml.com/v1", "bearer", 40),
        Provider("videogen", "openai", "OpenAI", "openai_video", "https://api.openai.com/v1", "bearer", 50),
        Provider("videogen", "custom_openai", "Custom (OpenAI API)", "openai_video", null, "bearer", 60)
    ];

    private static AiModelProvider Llm(
        string code,
        string name,
        string? baseUrl,
        string? defaultModel,
        int sortOrder,
        string bindingType = "openai_compatible",
        string authType = "bearer")
    {
        return Provider("llm", code, name, bindingType, baseUrl, authType, sortOrder, defaultModel);
    }

    private static AiModelProvider Embedding(
        string code,
        string name,
        string? baseUrl,
        string? defaultModel,
        int? defaultDimension,
        int sortOrder)
    {
        return Provider("embedding", code, name, code == "jina" ? "jina_embedding" : "openai_embedding", baseUrl, "bearer", sortOrder, defaultModel, defaultDimension);
    }

    private static AiModelProvider Provider(
        string service,
        string code,
        string name,
        string bindingType,
        string? baseUrl,
        string authType,
        int sortOrder,
        string? defaultModel = null,
        int? defaultDimension = null,
        string? defaultVoice = null)
    {
        return new AiModelProvider
        {
            ServiceType = service,
            ProviderCode = code,
            ProviderName = name,
            BindingType = bindingType,
            BaseUrl = baseUrl,
            AuthType = authType,
            IconKey = code,
            DefaultModel = defaultModel,
            DefaultDimension = defaultDimension,
            DefaultVoice = defaultVoice,
            SortOrder = sortOrder,
            IsEnabled = true,
            IsDeleted = false
        };
    }
}