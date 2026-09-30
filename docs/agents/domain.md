# 领域文档

本文件说明工程技能在探索代码库时应如何读取本仓库的领域文档。

## 探索前需要读取

- **`CONTEXT.md`**：读取仓库根目录中的文件；如果存在 **`CONTEXT-MAP.md`**，则读取该映射文件，并继续读取与当前主题相关的各上下文 `CONTEXT.md`。
- **`docs/adr/`**：读取与即将处理区域相关的 ADR。在多上下文仓库中，还要检查 `src/<context>/docs/adr/` 中的上下文级决策。

如果这些文件或目录不存在，请静默继续。不要提示缺失，也不要主动建议预先创建。`/domain-modeling` 技能会在实际解决术语或决策问题时按需创建它们。

## 文件结构

本仓库采用单上下文布局：

```text
/
├── CONTEXT.md
├── docs/adr/
│   ├── 0001-event-sourced-orders.md
│   └── 0002-postgres-for-write-model.md
└── src/
```

多上下文仓库会在根目录使用 `CONTEXT-MAP.md`，并通过该文件指向每个上下文自己的 `CONTEXT.md`：

```text
/
├── CONTEXT-MAP.md
├── docs/adr/                          ← 系统级决策
└── src/
    ├── ordering/
    │   ├── CONTEXT.md
    │   └── docs/adr/                  ← 上下文级决策
    └── billing/
        ├── CONTEXT.md
        └── docs/adr/                  ← 上下文级决策
```

## 使用术语表中的词汇

当输出中需要命名领域概念时，例如 issue 标题、重构建议、假设或测试名称，使用 `CONTEXT.md` 中定义的术语。不要改用术语表明确排除的同义词。

如果所需概念尚未进入术语表，这通常说明存在以下情况之一：正在引入项目未使用的语言，此时应重新考虑；或者确实存在术语缺口，此时记录该缺口并交由 `/domain-modeling` 处理。

## 标记 ADR 冲突

如果输出与现有 ADR 冲突，必须明确说明，而不是静默覆盖。例如：

> _与 ADR-0007（事件溯源订单）冲突，但值得重新讨论，因为……_
