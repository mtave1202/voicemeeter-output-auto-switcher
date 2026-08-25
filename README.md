# VoiceMeeter Output Auto Switcher

Windows の再生デバイス接続状態を監視し、VoiceMeeter Banana の Hardware Output **A2 / A3** を自動で切り替える常駐アプリです。

Windows の標準出力先は **VoiceMeeter Input** に固定したまま、Bluetooth イヤホンなどの実再生先だけを自動管理します。

## できること

- 管理対象デバイスの接続 / 切断を検知して A2 / A3 を再構成
- 優先順位に従って最大 2 台を割り当て（3 台目以降は待機）
- A2 / A3 変更時のみ Audio Engine を再起動（不要な Restart を抑制）
- タスクトレイ常駐、設定画面、手動 Restart、Windows 起動時の自動起動
- 動作ログのファイル出力

**A1 は本アプリから変更しません。**

## 動作環境

- Windows 10 / 11（x64）
- [VoiceMeeter Banana](https://vb-audio.com/Voicemeeter/banana.htm)（起動中であること）
- ビルド時: [.NET 10 SDK](https://dotnet.microsoft.com/download)（Windows Desktop ワークロード含む）

> 開発時のターゲットは `net10.0-windows` です（要件当初の .NET 8 から、環境都合で変更）。

## クイックスタート

### 1. ビルド

```powershell
dotnet build VoiceMeeterOutputAutoSwitcher.slnx -c Release
```

### 2. インストール（常用する場合）

**exe 単体では動きません。** 出力フォルダ一式（exe / dll / `Assets` / json）が必要です。  
あわせて [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) がインストールされている必要があります。

1. 次のフォルダの **中身すべて** をコピーする

    `src\VoiceMeeterOutputAutoSwitcher\bin\Release\net10.0-windows\`

2. コピー先の例（個人利用向け・管理者権限不要）

    `%LocalAppData%\VoiceMeeterOutputAutoSwitcher\`

    PowerShell 例:

    ```powershell
    $src = "src\VoiceMeeterOutputAutoSwitcher\bin\Release\net10.0-windows\*"
    $dst = "$env:LOCALAPPDATA\VoiceMeeterOutputAutoSwitcher"
    New-Item -ItemType Directory -Force -Path $dst | Out-Null
    Copy-Item -Path $src -Destination $dst -Recurse -Force
    ```

3. コピー先の `VoiceMeeterOutputAutoSwitcher.exe` を起動する
4. トレイ → **設定** で管理対象デバイスを選び、必要なら **Windows 起動時に自動起動する** を ON にして **保存**

自動起動 ON で保存すると、現在起動中の exe パスがユーザーのスタートアップ（`HKCU\...\Run`）に登録されます。  
確認: タスクマネージャー → **スタートアップ アプリ**、または 設定 → アプリ → **スタートアップ**。

> 開発中の一時実行だけなら、コピーせず次でも可です。  
> `dotnet run --project src\VoiceMeeterOutputAutoSwitcher -c Release`

### 3. 初期設定

1. VoiceMeeter Banana を起動する
2. トレイアイコンを右クリック → **設定**
3. 自動管理したい再生デバイスに「管理」チェックを入れる
4. 優先順位を調整して **保存**

接続中の管理対象が、優先度順に A2 / A3 へ割り当てられます。

## トレイメニュー

| 項目                 | 内容                                         |
| -------------------- | -------------------------------------------- |
| 出力状態             | A1 / A2 / A3 の現在デバイス（A1 は表示のみ） |
| 設定                 | 管理対象・優先度・自動起動・反映待ち時間     |
| Audio Engineを再起動 | 手動で `Command.Restart`                     |
| VoiceMeeterを開く    | インストール済み VoiceMeeter を起動          |
| 終了                 | アプリ終了                                   |

## 設定・ログの保存場所

| 種別 | パス                                                                           |
| ---- | ------------------------------------------------------------------------------ |
| 設定 | `%AppData%\VoicemeeterOutputAutoSwitcher\settings.json`                        |
| ログ | `%LocalAppData%\VoicemeeterOutputAutoSwitcher\logs\`（日次ファイル、14日保持） |

設定の識別子は表示名ではなく **Windows Endpoint ID** です。

## プロジェクト構成

```text
src/
  VoiceMeeterOutputAutoSwitcher/              WPF トレイアプリ
  VoiceMeeterOutputAutoSwitcher.Application/  OutputRoutingService など
  VoiceMeeterOutputAutoSwitcher.Core/         RoutingPolicy（単体テスト可能）
  VoiceMeeterOutputAutoSwitcher.Infrastructure/
    Windows Audio 監視 (NAudio)
    VoiceMeeter Remote API クライアント
    JSON 設定 / ファイルログ / 自動起動登録
tests/
  VoiceMeeterOutputAutoSwitcher.Core.Tests/
tools/
  VoiceMeeterApiSpike/         Phase1 API 確認用
  WindowsAudioDeviceSpike/     Phase2 デバイス監視確認用
  OutputRoutingSpike/          Phase4 自動同期確認用
```

## テスト

```powershell
dotnet test VoiceMeeterOutputAutoSwitcher.slnx -c Release
```

## 補足

- VoiceMeeter Remote API の DLL（`VoicemeeterRemote64.dll`）は VoiceMeeter 本体に同梱されます。別途 SDK の入手は不要です。
- 本リポジトリに API キーや認証情報は含みません。ユーザー設定・ログは AppData 側にのみ保存されます。
- VoiceMeeter 自体のライセンスは VB-Audio の条件に従ってください。

## 要件ドキュメント

詳細な要件・MVP 条件は [`voicemeeter-output-auto-switcher-requirements.md`](./voicemeeter-output-auto-switcher-requirements.md) を参照してください。

## MVP 対象外（現時点）

- VoiceMeeter Potato 対応
- A1 の自動管理
- Bluetooth ペアリング操作
- Windows 標準出力デバイス自体の変更
- デバイスごとの音量 / EQ 自動調整
- 自動アップデート
