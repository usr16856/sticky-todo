# AGENTS.md

- 本次修改的驗證依影響範圍與風險選擇；規劃驗證前讀 `docs/agent-workflows/verification-policy.md`，回報理由、實際結果及未測項目。
- Debug 版本本機開發／測試保留完整 log 顯示，不由 AI 擅自遮蔽或停印；Release、憑證存取與外部輸出另依原限制，詳見 `docs/agent-workflows/verification-policy.md`。


## AI 角色與工作交接

- 同一需求只保留一個實作主責。使用者已明確指定 Codex 或 Copilot，或目前任務已有可核實的主責選擇時，沿用且不重問；新需求未指定且無法從目前任務確認主責時，先問一次由 Codex 直接實作或交接 Copilot，不自行預設 Copilot。選定後依既定分工執行、交接與審查；本規則不新增執行授權，也不放寬既有安全、驗證或確認門檻。
- 有跨 AI 交接時，先讀 `docs/agent-workflows/ai-handoff.md`，核對 task/revision、唯一主責、精確 Git 與 dirty 檔快照、已執行命令和證據，再接手。
- 既有專案產品政策、工具安全限制、測試門檻、branch／報告建立與使用者確認要求繼續生效。交接格式不授權自動外傳、交易、部署、發布、commit、push 或 merge。
- 只在使用者要求交接時建立或更新交接檔。純問答、只讀審查不自動產出一套報告；若既有專案已要求報告，仍依原要求辦理。
- 從其他 checkout 複製的範例路徑、圖譜 project ID、測試結果與產物不能直接當成本 checkout 的證據；先核對來源與快照，不一致時回報，不能自動轉去修改其他目錄。

## 使用者路線圖

- `TODO.md` 由使用者維護；除非使用者明確要求，保持唯讀，不自動新增、重排或勾選。完成開發或接手工作不代表授權修改路線圖。
- 不把整份 TODO 複製到其他 repository、交接檔或持久記憶；只引用本次相關事項。
