# ADR 0005：消息总线选型与商业授权红线

- 状态：已采纳（选型约束，尚未引入消息总线）

## 背景
2025-2026 多个 .NET 常用库变更授权：
- MediatR v13.0.0 起 RPL-1.5 / 商业双授权，v12.5.0 及更早永久 Apache-2.0。
- MassTransit v9.0.0 起商业授权并需运行时 license key，v8 及更早永久 Apache-2.0。
- FluentAssertions v8+、ImageSharp v3+、NBomber v5+ 亦转向商业。

## 决策
- **当前项目不引入任何消息总线**，模块间通过进程内契约（`BlazorBoilerplate.ModuleAbstractions`）与 HTTP 端点协作。
- 明确禁用 RabbitMQ / MassTransit v9+。
- 若后续需要 CQRS/事件分发，优先 MIT/Apache-2.0 方案：`Mediator`（martinothamar，源生成、AOT 友好）或 Wolverine（MIT），或自研极简分发器。
- 测试断言优先 Shouldly / AwesomeAssertions，避免 FluentAssertions v8+。

## 后果
- 保持最小外部依赖与许可清晰；避免运行时 license key 带来的部署负担。
- 跨服务异步解耦能力缺失，需要时再单独做 ADR 与技术验证。
