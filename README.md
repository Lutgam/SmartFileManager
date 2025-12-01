📂 Smart File Manager (智慧型檔案管家)

指導教授： 吳帆 教授

開發平台： Windows (基於 .NET 8 / C#)

技術核心： C# + WebView2 + ML.NET (No-Python Architecture)

📖 專案簡介 (Introduction)

本專案旨在解決傳統檔案總管「難以搜尋內容」與「無法視覺化關聯」的痛點。我們開發了一款結合 AI 內容感知 與 多維度視圖 的新型態檔案管理工具。

系統提供 「雙模組切換」 功能，使用者可依據當下需求，在「關聯圖譜」與「傳統列表」間自由切換。

✨ 核心亮點 (Key Features)

雙視圖切換 (Dual View System)：

關聯視圖 (Graph)：仿 Anytype 風格，以節點網絡呈現檔案關聯，支援拖曳分類。

畫廊視圖 (Gallery)：仿 macOS Finder 風格，提供大尺寸即時預覽 (PDF 翻頁/Office 檢視)。

AI 內容識別 (Local AI)：
內建 ML.NET 深度學習引擎，自動辨識圖片內容（如：貓、風景）與文件語意。

混合式介面 (Hybrid UI)：
結合 C# 的系統底層效能與 WebView2 的動態渲染技術。

純 C# 架構：完全遵循企業級開發規範，不依賴 Python 環境。

🛠️ 技術架構 (Tech Stack)

層級

技術方案

說明

App 核心

C# / .NET 8

負責檔案監控、資料庫存取、視圖切換邏輯

前端視覺

WebView2 + Vis.js

負責渲染 Graph View 與 Gallery View 的動態切換

AI 引擎

ML.NET + ONNX

執行 YOLOv8 與 ResNet 模型進行影像辨識

資料庫

SQLite

儲存檔案 Metadata、Hash 值與標籤關聯

🚀 快速開始 (Quick Start)

1. 環境需求

Windows: Visual Studio 2022 (需安裝 .NET Desktop Development)

Mac: VS Code + C# Dev Kit + .NET 8 SDK

2. 下載與執行

git clone [https://github.com/你的GitHub帳號/SmartFileManager.git](https://github.com/你的GitHub帳號/SmartFileManager.git)
cd SmartFileManager
dotnet restore
dotnet run --project src/SmartFileManager.csproj


👥 團隊分工 (Team)

成員

角色

負責項目

組員 A

Core Arch

系統架構搭建、WebView2 整合、GitHub 維護

組員 B

Frontend

雙視圖介面設計 (HTML/CSS/JS)、互動邏輯優化

組員 C

Backend

SQLite 資料庫設計、檔案監控 (File Watcher)

組員 D

AI Model

ML.NET 模型訓練、資料集收集