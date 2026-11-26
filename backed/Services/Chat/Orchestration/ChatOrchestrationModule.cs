namespace AiAgent.Backend.Services.Chat.Orchestration;

internal static class ChatOrchestrationModule
{
    internal static IServiceCollection AddChatOrchestration(this IServiceCollection services)
    {
        services.AddSingleton<IChatOrchestrator, ChatOrchestrator>();
        services.AddSingleton<ChatDeliveryService>();
        services.AddSingleton<ChatCommandStore>();
        services.AddSingleton<ChatTurnPreparation>();
        services.AddSingleton<ChatRunFinalizer>();
        services.AddSingleton<ChatTurnExecutor>();
        services.AddSingleton<IChatDispatcher, ChatDispatcher>();
        services.AddSingleton<ChatDeliveryWorker>();
        services.AddHostedService(sp => sp.GetRequiredService<ChatDeliveryWorker>());
        return services;
    }
}
