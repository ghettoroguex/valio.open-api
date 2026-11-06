---
title: CLI 概览
description: valio 命令行界面。
translationKey: cli-overview
---

`valio` 是一个由 `ValIO.Cli` 构建的自包含可执行文件。同一个二进制
负责解析 OpenAPI 架构、运行生成器插件，并将结果输出为目录树或 zip
归档。

## 子命令

- [`valio generate`](/zh-cn/cli/generate/) —— 从 OpenAPI 架构生成代码
- [`valio validate`](/zh-cn/cli/validate/) —— 解析架构并打印元数据

## 全局选项

- `--lang <代码>` —— 用于显示消息的界面语言。可选值：`en`、`ru`、
  `de`、`zh-CN`。也会读取环境变量 `VALIO_LANG`。

## 退出码

| 退出码 | 含义                            |
|--------|---------------------------------|
| 0      | 成功                            |
| 1      | 解析、验证或 IO 失败            |
