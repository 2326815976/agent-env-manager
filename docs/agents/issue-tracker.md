# 问题跟踪：GitHub

本仓库的 issue 和 spec 存放在 GitHub Issues 中。所有相关操作均通过 `gh` CLI 完成。

## 约定

- **创建 issue**：执行 `gh issue create --title "..." --body "..."`。多行正文使用 heredoc。
- **读取 issue**：执行 `gh issue view <number> --comments`，再通过 `jq` 过滤评论，并同时获取标签。
- **列出 issue**：执行 `gh issue list --state open --json number,title,body,labels,comments --jq '[.[] | {number, title, body, labels: [.labels[].name], comments: [.comments[].body]}]'`，并按需添加 `--label` 和 `--state` 过滤条件。
- **评论 issue**：执行 `gh issue comment <number> --body "..."`。
- **添加或移除标签**：执行 `gh issue edit <number> --add-label "..."` 或 `--remove-label "..."`。
- **关闭 issue**：执行 `gh issue close <number> --comment "..."`。

在仓库克隆目录中运行时，`gh` 会根据 `git remote -v` 自动推断目标仓库。

## 将 Pull Request 作为分诊入口

**将 PR 作为需求入口：否。** _如果本仓库将外部 PR 视为功能请求，请改为 `是`；`/triage` 会读取该标志。_

设置为 `是` 后，PR 将使用与 issue 相同的标签和状态，并通过对应的 `gh pr` 命令处理：

- **读取 PR**：执行 `gh pr view <number> --comments`，并使用 `gh pr diff <number>` 查看差异。
- **列出待分诊的外部 PR**：执行 `gh pr list --state open --json number,title,body,labels,author,authorAssociation,comments`，仅保留 `authorAssociation` 为 `CONTRIBUTOR`、`FIRST_TIME_CONTRIBUTOR` 或 `NONE` 的记录，排除 `OWNER`、`MEMBER` 和 `COLLABORATOR`。
- **评论、添加标签或关闭**：执行 `gh pr comment`、`gh pr edit --add-label`、`gh pr edit --remove-label` 或 `gh pr close`。

GitHub 的 issue 和 PR 共用同一编号空间，因此单独的 `#42` 可能是 issue，也可能是 PR。先执行 `gh pr view 42` 判断，再回退到 `gh issue view 42`。

## 当技能要求“发布到问题跟踪器”

创建一个 GitHub issue。

## 当技能要求“获取相关 ticket”

执行 `gh issue view <number> --comments`。

## Wayfinding 操作

供 `/wayfinder` 使用。**map** 是一个 issue，**child ticket** 是由它关联的子 issue。

- **Map**：创建一个带有 `wayfinder:map` 标签的 issue，正文包含 Notes、Decisions-so-far 和 Fog。执行 `gh issue create --label wayfinder:map`。
- **Child ticket**：通过 GitHub sub-issue API 将 issue 关联到 map。若仓库未启用 sub-issue，则将子 issue 加入 map 正文的任务列表，并在子 issue 正文顶部添加 `Part of #<map>`。标签使用 `wayfinder:<type>`，其中类型为 `research`、`prototype`、`grilling` 或 `task`。issue 被领取后，将其分配给负责的开发人员。
- **阻塞关系**：优先使用 GitHub 原生 issue dependencies，这是规范且可在界面中直接查看的表示方式。执行 `gh api --method POST repos/<owner>/<repo>/issues/<child>/dependencies/blocked_by -F issue_id=<blocker-db-id>` 添加阻塞边；`<blocker-db-id>` 是阻塞 issue 的数字数据库 ID，可通过 `gh api repos/<owner>/<repo>/issues/<n> --jq .id` 获取，而不是 `#number` 或 `node_id`。GitHub 通过 `issue_dependencies_summary.blocked_by` 报告仍然打开的阻塞项，该字段代表当前实时阻塞门禁。如果无法使用 dependencies，则在子 issue 正文顶部添加 `Blocked by: #<n>, #<n>`。当所有阻塞 issue 都关闭后，该 ticket 即解除阻塞。
- **Frontier 查询**：列出 map 下仍处于打开状态的子 issue，可限定到 map 的 sub-issues 或任务列表；排除存在未关闭阻塞项或已有 assignee 的 issue，并按 map 中的顺序选择第一个可用 ticket。
- **领取**：执行 `gh issue edit <n> --add-assignee @me`，这是该会话的第一次写入。
- **解决**：先执行 `gh issue comment <n> --body "<answer>"`，再执行 `gh issue close <n>`，最后将上下文指针（要点摘要和链接）追加到 map 的 Decisions-so-far 中。
