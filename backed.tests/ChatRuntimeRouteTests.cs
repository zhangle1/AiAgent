using AiAgent.Backend.Services.CodeRepository;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using System.Reflection;

namespace AiAgent.Backend.Tests;

public sealed class ChatRuntimeRouteTests
{
    [Fact]
    public void BaseActionsUseControllerRouteWithoutAppendingProjectId()
    {
        var controllerRoute = typeof(ChatRuntimeAppService)
            .GetCustomAttributes<RouteAttribute>(inherit: true)
            .Single()
            .Template;

        Assert.Equal("api/v1/code-runtime/projects/{projectId:long}/chat-runs", controllerRoute);

        foreach (var methodName in new[] { nameof(ChatRuntimeAppService.List), nameof(ChatRuntimeAppService.Prepare) })
        {
            var method = typeof(ChatRuntimeAppService).GetMethod(methodName)!;
            var route = method.GetCustomAttributes<HttpMethodAttribute>(inherit: true).Single();
            Assert.Equal(string.Empty, route.Template);
        }
    }
}
