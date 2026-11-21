using BlazorBoilerplate.Domain.Events;
using Microsoft.Extensions.Logging;

namespace BlazorBoilerplate.Modules.Demo;

/// <summary>
/// 模块内部实体事件订阅示例（实现 <see cref="IConsumer{T}"/>）：
/// 仅限本模块内部联动（因为 <see cref="DemoNote"/> 属于 Demo 模块私有 ALC）。
/// 如需跨模块通知，请使用 <see cref="IChannelBus"/> 按字符串主题（Topic）广播，无需污染宿主 Domain。
/// </summary>
public sealed class DemoEntityConsumer(ILogger<DemoEntityConsumer> logger)
    : IConsumer<EntityInsertedEvent<DemoNote>>
{
    public Task HandleAsync(EntityInsertedEvent<DemoNote> domainEvent, CancellationToken cancellationToken)
    {
        logger.LogInformation("DemoEntityConsumer captured EntityInsertedEvent<DemoNote>: {Id} - {Title}",
            domainEvent.Entity.Id, domainEvent.Entity.Title);
        return Task.CompletedTask;
    }
}
