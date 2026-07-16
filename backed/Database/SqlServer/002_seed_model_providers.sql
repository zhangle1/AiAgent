SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

DECLARE @Providers TABLE (
    ServiceType NVARCHAR(32) NOT NULL,
    ProviderCode NVARCHAR(64) NOT NULL,
    ProviderName NVARCHAR(128) NOT NULL,
    BindingType NVARCHAR(64) NOT NULL,
    BaseUrl NVARCHAR(512) NULL,
    AuthType NVARCHAR(32) NOT NULL,
    IconKey NVARCHAR(64) NULL,
    DefaultModel NVARCHAR(128) NULL,
    DefaultDimension INT NULL,
    DefaultVoice NVARCHAR(64) NULL,
    SortOrder INT NOT NULL
);

INSERT INTO @Providers (ServiceType, ProviderCode, ProviderName, BindingType, BaseUrl, AuthType, IconKey, DefaultModel, DefaultDimension, DefaultVoice, SortOrder)
VALUES
('llm', 'aihubmix', 'AiHubMix', 'openai_compatible', 'https://aihubmix.com/v1', 'bearer', 'aihubmix', NULL, NULL, NULL, 10),
('llm', 'anthropic', 'Anthropic', 'anthropic', 'https://api.anthropic.com', 'x-api-key', 'anthropic', 'claude-sonnet-4-20250514', NULL, NULL, 20),
('llm', 'azure_openai', 'Azure OpenAI', 'azure_openai', NULL, 'api-key', 'azure', NULL, NULL, NULL, 30),
('llm', 'byteplus', 'BytePlus', 'openai_compatible', 'https://ark.ap-southeast.bytepluses.com/api/v3', 'bearer', 'bytedance', NULL, NULL, NULL, 40),
('llm', 'byteplus_coding', 'BytePlus Coding Plan', 'openai_compatible', 'https://ark.ap-southeast.bytepluses.com/api/v3', 'bearer', 'bytedance', NULL, NULL, NULL, 50),
('llm', 'custom_anthropic', 'Custom (Anthropic API)', 'anthropic', NULL, 'x-api-key', 'anthropic', NULL, NULL, NULL, 60),
('llm', 'custom_openai', 'Custom (OpenAI API)', 'openai_compatible', NULL, 'bearer', 'openai', NULL, NULL, NULL, 70),
('llm', 'dashscope', 'DashScope', 'openai_compatible', 'https://dashscope.aliyuncs.com/compatible-mode/v1', 'bearer', 'qwen', 'qwen-plus', NULL, NULL, 80),
('llm', 'deepseek', 'DeepSeek', 'openai_compatible', 'https://api.deepseek.com', 'bearer', 'deepseek', 'deepseek-chat', NULL, NULL, 90),
('llm', 'gemini', 'Gemini', 'openai_compatible', 'https://generativelanguage.googleapis.com/v1beta/openai', 'bearer', 'gemini', 'gemini-2.5-flash', NULL, NULL, 100),
('llm', 'github_copilot', 'GitHub Copilot', 'github_copilot', NULL, 'oauth', 'githubcopilot', NULL, NULL, NULL, 110),
('llm', 'groq', 'Groq', 'openai_compatible', 'https://api.groq.com/openai/v1', 'bearer', 'groq', NULL, NULL, NULL, 120),
('llm', 'lemonade', 'Lemonade', 'openai_compatible', 'http://localhost:8000/api/v1', 'bearer', 'lemonade', NULL, NULL, NULL, 130),
('llm', 'llama_cpp', 'llama.cpp', 'openai_compatible', 'http://localhost:8080/v1', 'bearer', 'llama', NULL, NULL, NULL, 140),
('llm', 'lm_studio', 'LM Studio', 'openai_compatible', 'http://localhost:1234/v1', 'bearer', 'lmstudio', NULL, NULL, NULL, 150),
('llm', 'minimax', 'MiniMax', 'openai_compatible', 'https://api.minimax.io/v1', 'bearer', 'minimax', NULL, NULL, NULL, 160),
('llm', 'minimax_anthropic', 'MiniMax (Anthropic)', 'anthropic', 'https://api.minimax.io/anthropic', 'x-api-key', 'minimax', NULL, NULL, NULL, 170),
('llm', 'mistral', 'Mistral', 'openai_compatible', 'https://api.mistral.ai/v1', 'bearer', 'mistral', NULL, NULL, NULL, 180),
('llm', 'moonshot', 'Moonshot', 'openai_compatible', 'https://api.moonshot.cn/v1', 'bearer', 'moonshot', NULL, NULL, NULL, 190),
('llm', 'ollama', 'Ollama', 'openai_compatible', 'http://localhost:11434/v1', 'bearer', 'ollama', NULL, NULL, NULL, 200),
('llm', 'openai', 'OpenAI', 'openai', 'https://api.openai.com/v1', 'bearer', 'openai', 'gpt-4.1', NULL, NULL, 210),
('llm', 'openrouter', 'OpenRouter', 'openai_compatible', 'https://openrouter.ai/api/v1', 'bearer', 'openrouter', NULL, NULL, NULL, 220),
('llm', 'siliconflow', 'SiliconFlow', 'openai_compatible', 'https://api.siliconflow.cn/v1', 'bearer', 'siliconflow', NULL, NULL, NULL, 230),
('llm', 'zhipu', 'Zhipu AI', 'openai_compatible', 'https://open.bigmodel.cn/api/paas/v4', 'bearer', 'zhipu', NULL, NULL, NULL, 240),

('embedding', 'openai', 'OpenAI', 'openai_embedding', 'https://api.openai.com/v1', 'bearer', 'openai', 'text-embedding-3-small', 1536, NULL, 10),
('embedding', 'openai_compatible', 'Custom (OpenAI API)', 'openai_embedding', NULL, 'bearer', 'openai', NULL, NULL, NULL, 20),
('embedding', 'dashscope', 'DashScope', 'dashscope_embedding', 'https://dashscope.aliyuncs.com/compatible-mode/v1', 'bearer', 'qwen', 'text-embedding-v4', 1024, NULL, 30),
('embedding', 'jina', 'Jina', 'jina_embedding', 'https://api.jina.ai/v1', 'bearer', 'jina', 'jina-embeddings-v3', 1024, NULL, 40),
('embedding', 'ollama', 'Ollama', 'ollama_embedding', 'http://localhost:11434', 'bearer', 'ollama', NULL, NULL, NULL, 50),
('embedding', 'cohere', 'Cohere', 'cohere_embedding', 'https://api.cohere.com/v2', 'bearer', 'cohere', NULL, NULL, NULL, 60),
('embedding', 'siliconflow', 'SiliconFlow', 'openai_embedding', 'https://api.siliconflow.cn/v1', 'bearer', 'siliconflow', NULL, NULL, NULL, 70),
('embedding', 'gemini', 'Gemini', 'openai_embedding', 'https://generativelanguage.googleapis.com/v1beta/openai', 'bearer', 'gemini', NULL, NULL, NULL, 80),
('embedding', 'openrouter', 'OpenRouter', 'openai_embedding', 'https://openrouter.ai/api/v1', 'bearer', 'openrouter', NULL, NULL, NULL, 90),
('embedding', 'lm_studio', 'LM Studio', 'openai_embedding', 'http://localhost:1234/v1', 'bearer', 'lmstudio', NULL, NULL, NULL, 100),

('search', 'tavily', 'Tavily', 'tavily_search', 'https://api.tavily.com', 'bearer', 'tavily', NULL, NULL, NULL, 10),
('search', 'brave', 'Brave Search', 'brave_search', 'https://api.search.brave.com', 'x-subscription-token', 'brave', NULL, NULL, NULL, 20),
('search', 'serper', 'Serper', 'serper_search', 'https://google.serper.dev', 'x-api-key', 'serper', NULL, NULL, NULL, 30),
('search', 'searxng', 'SearXNG', 'searxng_search', NULL, 'none', 'searxng', NULL, NULL, NULL, 40),
('search', 'jina', 'Jina', 'jina_search', 'https://s.jina.ai', 'bearer', 'jina', NULL, NULL, NULL, 50),
('search', 'duckduckgo', 'DuckDuckGo', 'duckduckgo_search', NULL, 'none', 'duckduckgo', NULL, NULL, NULL, 60),
('search', 'exa', 'Exa', 'exa_search', 'https://api.exa.ai', 'x-api-key', 'exa', NULL, NULL, NULL, 70),
('search', 'perplexity', 'Perplexity', 'perplexity_search', 'https://api.perplexity.ai', 'bearer', 'perplexity', NULL, NULL, NULL, 80),
('search', 'openrouter', 'OpenRouter', 'openrouter_search', 'https://openrouter.ai/api/v1', 'bearer', 'openrouter', NULL, NULL, NULL, 90),
('search', 'baidu', 'Baidu', 'baidu_search', NULL, 'bearer', 'baidu', NULL, NULL, NULL, 100),

('tts', 'openai', 'OpenAI', 'openai_tts', 'https://api.openai.com/v1', 'bearer', 'openai', 'gpt-4o-mini-tts', NULL, 'alloy', 10),
('tts', 'azure_openai', 'Azure OpenAI', 'azure_tts', NULL, 'api-key', 'azure', NULL, NULL, NULL, 20),
('tts', 'groq', 'Groq', 'openai_tts', 'https://api.groq.com/openai/v1', 'bearer', 'groq', NULL, NULL, NULL, 30),
('tts', 'openrouter', 'OpenRouter', 'openrouter_tts', 'https://openrouter.ai/api/v1', 'bearer', 'openrouter', NULL, NULL, NULL, 40),
('tts', 'siliconflow', 'SiliconFlow', 'openai_tts', 'https://api.siliconflow.cn/v1', 'bearer', 'siliconflow', NULL, NULL, NULL, 50),
('tts', 'elevenlabs', 'ElevenLabs', 'elevenlabs_tts', 'https://api.elevenlabs.io/v1', 'xi-api-key', 'elevenlabs', NULL, NULL, NULL, 60),
('tts', 'gemini', 'Gemini', 'gemini_tts', 'https://generativelanguage.googleapis.com/v1beta', 'bearer', 'gemini', NULL, NULL, NULL, 70),

('stt', 'openai', 'OpenAI', 'openai_stt', 'https://api.openai.com/v1', 'bearer', 'openai', 'gpt-4o-mini-transcribe', NULL, NULL, 10),
('stt', 'azure_openai', 'Azure OpenAI', 'azure_stt', NULL, 'api-key', 'azure', NULL, NULL, NULL, 20),
('stt', 'groq', 'Groq', 'openai_stt', 'https://api.groq.com/openai/v1', 'bearer', 'groq', NULL, NULL, NULL, 30),
('stt', 'openrouter', 'OpenRouter', 'openrouter_stt', 'https://openrouter.ai/api/v1', 'bearer', 'openrouter', NULL, NULL, NULL, 40),
('stt', 'siliconflow', 'SiliconFlow', 'openai_stt', 'https://api.siliconflow.cn/v1', 'bearer', 'siliconflow', NULL, NULL, NULL, 50),
('stt', 'deepgram', 'Deepgram', 'deepgram_stt', 'https://api.deepgram.com/v1', 'token', 'deepgram', NULL, NULL, NULL, 60),
('stt', 'gemini', 'Gemini', 'gemini_stt', 'https://generativelanguage.googleapis.com/v1beta', 'bearer', 'gemini', NULL, NULL, NULL, 70),

('imagegen', 'openai', 'OpenAI', 'openai_image', 'https://api.openai.com/v1', 'bearer', 'openai', 'gpt-image-1', NULL, NULL, 10),
('imagegen', 'azure_openai', 'Azure OpenAI', 'azure_image', NULL, 'api-key', 'azure', NULL, NULL, NULL, 20),
('imagegen', 'openrouter', 'OpenRouter', 'openrouter_image', 'https://openrouter.ai/api/v1', 'bearer', 'openrouter', NULL, NULL, NULL, 30),
('imagegen', 'siliconflow', 'SiliconFlow', 'openai_image', 'https://api.siliconflow.cn/v1', 'bearer', 'siliconflow', NULL, NULL, NULL, 40),
('imagegen', 'gemini', 'Gemini', 'gemini_image', 'https://generativelanguage.googleapis.com/v1beta', 'bearer', 'gemini', NULL, NULL, NULL, 50),
('imagegen', 'dashscope', 'DashScope', 'dashscope_image', 'https://dashscope.aliyuncs.com/api/v1', 'bearer', 'qwen', NULL, NULL, NULL, 60),

('videogen', 'dashscope', 'DashScope', 'dashscope_video', 'https://dashscope.aliyuncs.com/api/v1', 'bearer', 'qwen', NULL, NULL, NULL, 10),
('videogen', 'minimax', 'MiniMax', 'minimax_video', 'https://api.minimax.io/v1', 'bearer', 'minimax', NULL, NULL, NULL, 20),
('videogen', 'kling', 'Kling', 'kling_video', NULL, 'bearer', 'kling', NULL, NULL, NULL, 30),
('videogen', 'runway', 'Runway', 'runway_video', 'https://api.runwayml.com/v1', 'bearer', 'runway', NULL, NULL, NULL, 40),
('videogen', 'openai', 'OpenAI', 'openai_video', 'https://api.openai.com/v1', 'bearer', 'openai', NULL, NULL, NULL, 50),
('videogen', 'custom_openai', 'Custom (OpenAI API)', 'openai_video', NULL, 'bearer', 'openai', NULL, NULL, NULL, 60);

MERGE dbo.ai_model_provider AS target
USING @Providers AS source
ON target.ServiceType = source.ServiceType
    AND target.ProviderCode = source.ProviderCode
    AND target.IsDeleted = 0
WHEN MATCHED THEN
    UPDATE SET
        ProviderName = source.ProviderName,
        BindingType = source.BindingType,
        BaseUrl = source.BaseUrl,
        AuthType = source.AuthType,
        IconKey = source.IconKey,
        DefaultModel = source.DefaultModel,
        DefaultDimension = source.DefaultDimension,
        DefaultVoice = source.DefaultVoice,
        SortOrder = source.SortOrder,
        IsEnabled = 1,
        UpdatedAt = SYSUTCDATETIME()
WHEN NOT MATCHED BY TARGET THEN
    INSERT (ServiceType, ProviderCode, ProviderName, BindingType, BaseUrl, AuthType, IconKey, DefaultModel, DefaultDimension, DefaultVoice, SortOrder, IsEnabled, IsDeleted)
    VALUES (source.ServiceType, source.ProviderCode, source.ProviderName, source.BindingType, source.BaseUrl, source.AuthType, source.IconKey, source.DefaultModel, source.DefaultDimension, source.DefaultVoice, source.SortOrder, 1, 0);
