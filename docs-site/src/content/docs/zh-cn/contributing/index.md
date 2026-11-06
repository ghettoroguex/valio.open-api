---
title: 参与贡献
description: 编码规范与插件开发指南。
translationKey: contributing
---

ValIO 欢迎社区贡献 —— 新的生成器插件、为现有插件新增传输实现、修复
缺陷以及翻译。

## 本地搭建

```bash
git clone https://github.com/valio-projects/valio.open-api
cd valio
dotnet build ValIO.sln
dotnet test  ValIO.sln
```

需要 .NET 10 SDK（`global.json` 锁定为 `10.0.100`）。

## 编码规范

代码库强制执行若干不可妥协的规则 —— 完整内容见 `CODING_RULES.md`：

- **不允许保留死代码或 `[Obsolete]` 通道。** 一旦契约变更，调用方必
  须在同一次提交中跟进修改。
- **不允许使用兜底的 `catch (Exception)`**，除非用于补充上下文并重新
  抛出。校验器会让 `ValidationException` 向上冒泡。
- **任何绑定到 UI 的字符串都使用 resx 键**，不允许写死英文。
- **`AddValIO*` 扩展方法必须保持幂等**，所有前端都按照相同顺序调用
  相同的扩展集合。

## 编写新的生成器插件

插件是一个实现了 `IGeneratorPlugin` 的类：

```csharp
public sealed class MyLangGeneratorPlugin : IGeneratorPlugin
{
    public string LanguageId => "mylang";
    public string DisplayName => "MyLang";
    public IReadOnlyList<string> LanguageAliases => ["ml"];
    public IGeneratorOptionsDescriptor OptionsDescriptor { get; } = new MyLangOptionsDescriptor();

    public ICodeGenerationPipeline CreatePipeline(IServiceProvider services)
        => new CodeGenerationPipeline(new ICodeGenerationStep[]
        {
            new MyLangModelsStep(),
            new MyLangClientsStep(),
        });

    public void ConfigureServices(IServiceCollection services) { }
}
```

之后在 `ValIO.Plugins.Bootstrap.PluginsBootstrapExtensions` 中完成
注册，使所有前端都能自动加载。

## 添加翻译

将新的 `*.<culture>.resx` 文件放在
`src/ValIO.Localization/Resources/` 下已有文件旁边即可。缺失的键
会自动回退到默认（英文）资源包。

## Pull Request

- 每个 PR 仅包含一项逻辑变更。
- 任何涉及生成器的变更，请在描述中附上 `dotnet test` 的输出。
- 新的传输实现 / 校验器请在对应的
  `tests/ValIO.Generators.*.Tests/` 项目下补充示例集成测试。
