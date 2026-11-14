# ADR 0001：模块采用前后端同体的 Razor Class Library

- 状态：已采纳（2026-11-13，用户明确要求）

## 背景
早期设计把模块拆成后端类库 + 前端页面两个项目。用户指出「模块，应该是前端和后端都是同一个模块的哈，不应该是前端和后端分开哈」。

## 决策
一个模块 = 一个 `Microsoft.NET.Sdk.Razor` 项目，内部同时包含 Domain / Application / Infrastructure / Endpoints / Razor 页面 / `manifest.json` / 菜单声明。
模块通过 `[ModuleInitializer]` 或 `manifest.frontend.menuItems` 自注册前端元数据；Host 不拥有任何模块私有页面或 DTO。

## 后果
- 模块可独立开发、独立打包、独立版本化；删除模块时前端痕迹一并消失。
- WebHost 必须静态引用 RCL 才能让 Blazor 发现页面：`MapRazorComponents<App>().AddAdditionalAssemblies(...)` 与 `Router.AdditionalAssemblies` **两处**都要声明（清单集中在 `Components/ComponentAssemblies.cs`）。
- 运行时动态新增模块的页面自动发现尚未实现，属独立 POC（见 ROADMAP 批次 4）。
