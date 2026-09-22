# StickyTodo

繁體中文 Windows 桌面待辦便條紙。單機離線使用，以 Markdown 作為唯一待辦資料來源；沒有 Codex skill 或全域指令整合。

## 使用

- 桌面或開始功能表的 StickyTodo 捷徑開啟便條紙。
- 「新增待辦」可勾選一個或多個專案；儲存後每個專案各建立一筆可獨立管理的待辦。「編輯」可修改單筆內容或移動至另一個專案。
- 完成項目保持原位與編號，可取消完成或隱藏。
- 關閉視窗會縮到系統匣；設定或系統匣的「結束 StickyTodo」才會結束程式。
- 置頂與登入 Windows 時啟動預設關閉。重複啟動會顯示既有視窗。
- 底部右側滑桿即時調整主視窗不透明度（40～100%），並記住設定。Tab 可聚焦，方向鍵每次調整 1%，Home／End 切換最低／最高值；新增／編輯表單與快顯選單保持完全不透明，方便閱讀與輸入。
- 設定選單的「開啟待辦資料檔」使用 Windows 預設 App；「選擇 App 開啟…」可針對當次操作挑選其他編輯器，不在 StickyTodo 保存程式路徑。
- 編輯表單的「刪除待辦」須再次確認；刪除後重新編號，資料檔旁保留上一版備份。外部資料變更時停用刪除，避免刪錯項目。

## 位置

- 原始碼：`C:\Users\bench\git\StickyTodo`
- 資料：`C:\Users\bench\git\StickyTodo\TODO.md`
- 上一版資料：資料檔旁的 `TODO.md.bak`
- 程式：`%LOCALAPPDATA%\Programs\StickyTodo\StickyTodo.exe`
- 設定：`%LOCALAPPDATA%\StickyTodo\settings.json`

專案選單使用 `C:\Users\bench\git` 第一層非隱藏、非系統資料夾，排除以點開頭的資料夾及 `docs`、`artifacts`、`node_modules`、`bin`、`obj`。不要求 `.git`，`其他` 固定放最後；已有的歷史分組不因資料夾消失而刪除。新增視窗會記住上次勾選的專案組合；舊版設定的單一專案會自動沿用。

## 資料格式與保護

```markdown
# 我的待辦

## SpotCam
1. [ ] 首行內容
    第二行內容
2. [x] 已完成內容

## 其他
1. [ ] 其他事情
```

使用 UTF-8；CRLF、LF 及 UTF-8 BOM 均可讀取。每個專案編號從 1 連續排列。項目內換行使用四個空白縮排，空白延續行也需縮排。App 保留文字內容，不將 Markdown 符號渲染成富文字。

每次存檔使用排他鎖、版本雜湊、同目錄暫存檔及原子替換，保留上一版備份。App 每秒檢查外部變更。編輯中發現外部修改時保留草稿，可選擇重新載入並將草稿另存新待辦；不會以舊的項目位置覆蓋新資料。一般文字編輯器不遵守 App 鎖定協定，因此仍應避免在按儲存的同時於另一個程式儲存同一檔案。

格式錯誤時停用修改並顯示原因；原檔不會被重建或清空。修正後按重新載入。復原備份時先結束 App，再自行保留目前檔案並將 `.bak` 複製回 `TODO.md`。

## 建置與部署

需求：Windows、.NET 10 SDK。已安裝的個人 SDK 位於 `%LOCALAPPDATA%\Microsoft\dotnet`，腳本優先使用該位置，否則使用 PATH 的 dotnet。

```powershell
.\scripts\Build.ps1
.\scripts\Deploy.ps1 -SkipBuild
```

Build 先執行資料層測試，再產生符合本機架構的 self-contained Windows 發佈目錄。Deploy 複製發佈檔案並建立桌面與開始功能表捷徑，不修改資料、設定或開機啟動。更新前需結束已安裝 App。沒有自動 Git commit 或 push。

## 隔離桌面測試

主視窗保留 WindowChrome，使用 WPF `AllowsTransparency` 與 `Opacity`。WPF 會移除直接加入的 `WS_EX_LAYERED`，因此不直接呼叫 `SetLayeredWindowAttributes`，由 WPF 管理透明視窗。

App 接受 `--data-dir`、`--settings-dir`、`--project-root` 指定測試目錄。使用 `--data-dir` 時標題顯示「測試便條紙」並停用開機啟動操作。請同時指定獨立的設定目錄，避免影響正式設定。每個資料目錄只允許一個 App 執行個體。
