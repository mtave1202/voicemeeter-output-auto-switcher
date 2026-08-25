# VoiceMeeter Output Auto Switcher 要件定義

## 1. 目的

Windows の標準音声出力先を常に **VoiceMeeter Input**
に固定したまま、実際の再生先となる VoiceMeeter Banana の Hardware Output
を自動管理する。

現在手動で行っている以下の操作を不要にする。

-   Bluetooth イヤホン接続後の「Restart Audio Engine」
-   A2 / A3 への Bluetooth イヤホンの手動割り当て
-   Bluetooth イヤホンを変更するたびの VoiceMeeter 設定変更

## 2. 想定構成

``` text
Windows / 各アプリ
        │
        ▼
VoiceMeeter Input
        │
        ├── A1 : PC標準出力（固定）
        ├── A2 : 接続中イヤホン（自動）
        └── A3 : 接続中イヤホン（自動）
```

A1 は PC 標準出力として固定し、本アプリからは変更しない。

## 3. 基本要件

### 3.1 常駐

アプリは Windows 起動後、バックグラウンドで常駐する。

WPF
のメインウィンドウを常時表示する必要はなく、通常時はタスクトレイに格納する。

``` text
Windows起動
  ↓
Auto Switcher起動
  ↓
VoiceMeeter接続
  ↓
現在のAudio Device一覧取得
  ↓
A2/A3を同期
  ↓
Audio Device変更を監視
```

### 3.2 A1 固定

VoiceMeeter Banana の A1 は自動変更しない。

``` text
A1 = PC標準出力
```

A1 の設定は VoiceMeeter 側で管理し、本アプリから変更しない。

これにより、本アプリの不具合や Bluetooth
デバイスの状態にかかわらず、最低1つの出力先を確保する。

### 3.3 管理対象デバイス

自動管理対象となる Audio Device を複数登録できるようにする。

設定例：

``` json
{
  "managedDevices": [
    {
      "deviceId": "...",
      "name": "WF-1000XM5",
      "enabled": true,
      "priority": 1
    },
    {
      "deviceId": "...",
      "name": "AirPods Pro",
      "enabled": true,
      "priority": 2
    },
    {
      "deviceId": "...",
      "name": "EarFun Air Pro 4",
      "enabled": true,
      "priority": 3
    }
  ]
}
```

Bluetooth デバイスに限定せず、Windows 上の Playback Endpoint
を対象とする。

将来的には以下にも対応可能な構造とする。

-   Bluetooth イヤホン
-   USB ヘッドセット
-   USB DAC
-   HDMI Audio

## 4. Audio Device 監視

Windows Core Audio API を利用して、Playback Endpoint
の状態変化を監視する。

想定するイベント：

-   Device Added
-   Device Removed
-   Device State Changed
-   Device Property Changed

Bluetooth 接続イベントそのものを監視するのではなく、**Windows
の再生デバイスとして利用可能になったこと**を検知する。

実装には NAudio 等の利用を想定する。

## 5. デバイス接続時の処理

現在、

``` text
A1 = Speakers
A2 = -
A3 = -
```

の状態で WF-1000XM5 が Windows 上で利用可能になった場合：

``` text
WF-1000XM5
    ↓
Playback Endpoint = Active
    ↓
管理対象Deviceか判定
    ↓
A2へ設定
    ↓
VoiceMeeter Audio Engine Restart
```

最終状態：

``` text
A1 = Speakers
A2 = WF-1000XM5
A3 = -
```

## 6. 複数デバイス接続

2台接続されている場合：

``` text
A1 = Speakers
A2 = WF-1000XM5
A3 = AirPods Pro
```

3台以上接続された場合は、設定された `priority` の高い順に2台を使用する。

例：

``` text
priority 1 : WF-1000XM5
priority 2 : AirPods Pro
priority 3 : EarFun Air Pro 4
```

3台すべてが Active の場合：

``` text
A2 = WF-1000XM5
A3 = AirPods Pro

EarFun Air Pro 4 = 待機
```

「最後に接続したデバイスを優先」ではなく、明示的な優先順位方式を採用する。

## 7. デバイス切断時

例えば、

``` text
A2 = WF-1000XM5
A3 = AirPods Pro
```

から WF-1000XM5 を切断した場合：

``` text
WF-1000XM5
    ↓
Inactive / NotPresent
    ↓
現在Activeな管理対象を再評価
    ↓
A2/A3を再構成
```

結果：

``` text
A2 = AirPods Pro
A3 = -
```

3台接続されており、優先順位1位のデバイスが切断された場合は、待機していたデバイスを繰り上げる。

## 8. VoiceMeeter 連携

VB-Audio 公式の **VoiceMeeter Remote API** を使用する。

本アプリから以下を制御する。

-   A2 の出力デバイス設定
-   A3 の出力デバイス設定
-   Audio Engine Restart
-   必要に応じた現在状態の取得

A1 は制御対象外とする。

ドライバ形式は原則として **WDM** を使用する。

VoiceMeeter Remote API の DLL 呼び出し部分は専用クラスに隔離し、UI や
Windows Audio 監視処理から直接呼び出さない。

## 9. Audio Engine Restart

A2 / A3 の構成変更後、VoiceMeeter Audio Engine を自動再起動する。

VoiceMeeter Remote API の以下に相当する処理を利用する。

``` text
Command.Restart = 1
```

ただし、Device State Changed のたびに即座に再起動してはいけない。

Bluetooth 接続時には Windows
側で複数の状態変更イベントが短時間に発生する可能性があるため、debounce
処理を入れる。

``` text
Audio Device変更
    ↓
500～1500ms程度待機
    ↓
さらに変更があればタイマーをリセット
    ↓
状態確定
    ↓
A2/A3変更
    ↓
Audio Engine Restart
```

また、A2 / A3 の割り当て結果が現在値と同一の場合は、原則として Restart
を実行しない。

## 10. 起動時同期

アプリ起動時にも現在の Audio Device 状態を確認する。

例：

``` text
アプリ起動

Active:
  Speakers
  WF-1000XM5

↓

A1 = 変更しない
A2 = WF-1000XM5
A3 = -

↓

必要ならAudio Engine Restart
```

イベント監視だけではなく、**起動時同期 + イベント監視** の2つを持つ。

## 11. UI

通常はタスクトレイに常駐する。

タスクトレイの右クリックメニュー例：

``` text
VoiceMeeter Auto Switcher

出力状態
  A1: Speakers
  A2: WF-1000XM5
  A3: -

----------------
設定
Audio Engineを再起動
VoiceMeeterを開く
----------------
終了
```

A1 は VoiceMeeter
から取得可能であれば表示するが、本アプリからは変更しない。

## 12. 設定画面

Windows 上で検出できる Playback Device
を一覧表示し、自動管理対象を選択できるようにする。

例：

``` text
自動管理する出力デバイス

☑ WF-1000XM5       優先順位 1
☑ AirPods Pro      優先順位 2
☑ EarFun Air Pro   優先順位 3
☐ HDMI Audio
☐ Monitor Audio
```

要件：

-   デバイス名を手入力させない
-   管理対象の有効 / 無効を変更できる
-   優先順位を変更できる
-   設定内容を永続化する

## 13. デバイス識別

Windows Audio Device の内部識別には可能な限り **IMMDevice ID（Endpoint
ID）** を利用する。

表示名だけを識別キーにしない。

理由：

``` text
Headphones
Headphones
```

のような同名デバイスが存在する可能性があるため。

内部的には以下を分離して管理できる構造とする。

``` text
Windows Endpoint ID
Friendly Name
VoiceMeeter Device Name
```

VoiceMeeter Remote API へ出力デバイスを指定する際には、VoiceMeeter
側が認識しているデバイスとの対応付けを行う。

## 14. VoiceMeeter 未起動時

本アプリ起動時に VoiceMeeter が起動していない場合でも異常終了しない。

``` text
VoiceMeeter未起動

状態:
Disconnected

↓

一定時間後またはVoiceMeeter起動後
再接続
```

VoiceMeeter の自動起動は MVP では必須としない。

## 15. エラー処理

以下の状況でもアプリを異常終了させない。

-   Bluetooth Device が突然消えた
-   VoiceMeeter が終了した
-   VoiceMeeter Remote API 呼び出しに失敗した
-   Audio Engine Restart に失敗した
-   設定済み Device が存在しない
-   Windows Audio Device の状態が処理中に変化した

エラー内容をログへ記録し、可能な限り監視を継続する。

## 16. ログ

最低限、以下のようなログを残す。

``` text
2026-08-25 20:30:01 Device Active: WF-1000XM5
2026-08-25 20:30:02 Output changed
2026-08-25 20:30:02 A2 -> WF-1000XM5
2026-08-25 20:30:02 A3 -> Empty
2026-08-25 20:30:03 VoiceMeeter Audio Engine restarted
```

記録対象：

-   アプリ起動 / 終了
-   VoiceMeeter 接続 / 切断
-   Audio Device 状態変更
-   A2 / A3 割り当て変更
-   Audio Engine Restart
-   API エラー
-   例外

ログファイルはローテーションまたは一定期間で削除できる構造が望ましい。

## 17. 技術構成

想定技術：

``` text
.NET 8
WPF
NAudio
VoiceMeeter Remote API
Microsoft.Extensions.DependencyInjection
Microsoft.Extensions.Logging
```

想定構造：

``` text
Presentation
 ├─ TrayIcon
 └─ SettingsWindow

Application
 └─ OutputRoutingService

Domain
 ├─ AudioDevice
 ├─ ManagedDevice
 └─ RoutingState

Infrastructure
 ├─ WindowsAudioDeviceWatcher
 ├─ VoiceMeeterRemoteClient
 ├─ JsonSettingsRepository
 └─ Logging
```

過剰な Clean Architecture 化は行わない。

ただし、以下の責務は明確に分離する。

1.  Windows Audio Device の列挙・監視
2.  管理対象デバイスと優先順位の判定
3.  A2 / A3 の割り当て決定
4.  VoiceMeeter Remote API 操作
5.  UI / タスクトレイ
6.  設定永続化

## 18. MVP 完成条件

以下をすべて満たした時点を MVP 完成とする。

-   Windows 起動時に常駐できる
-   A1 には一切変更を加えない
-   管理対象イヤホンを複数登録できる
-   接続された管理対象デバイスを自動検知できる
-   A2 / A3 へ最大2台まで自動設定できる
-   3台以上接続時は優先順位に従って2台を選択できる
-   切断時に A2 / A3 を自動再構成できる
-   接続・切断後に必要に応じて VoiceMeeter Audio Engine
    を自動再起動できる
-   複数イベント発生時に Restart を連発しない
-   割り当てに変更がない場合は不要な Restart を行わない
-   VoiceMeeter 未起動・デバイス消失で異常終了しない
-   タスクトレイから現在の A1 / A2 / A3 を確認できる
-   タスクトレイから手動で Audio Engine Restart ができる
-   設定を次回起動時にも保持する
-   基本的な動作ログを記録する

## 19. 推奨実装順序

### Phase 1: VoiceMeeter API Spike

最初に小さなコンソールアプリを作り、VoiceMeeter Remote API
の動作確認を行う。

確認項目：

1.  VoiceMeeter への接続
2.  現在状態の取得
3.  A2 への任意デバイス割り当て
4.  A3 への任意デバイス割り当て
5.  Audio Engine Restart

ここが成功すれば、VoiceMeeter 連携部分の技術リスクは大幅に下がる。

### Phase 2: Windows Audio Device 監視

NAudio / Core Audio API を利用して以下を確認する。

1.  Playback Device 一覧取得
2.  Endpoint ID 取得
3.  Active / Inactive 判定
4.  Bluetooth イヤホン接続時のイベント検知
5.  Bluetooth イヤホン切断時のイベント検知

### Phase 3: Routing Service

Windows Audio Device の状態から A2 / A3 の割り当てを決定する。

この部分は Windows API や VoiceMeeter API
に依存させず、単体テスト可能なロジックとして実装する。

### Phase 4: 自動同期

以下を接続する。

``` text
Audio Device変更
      ↓
Debounce
      ↓
Routing Service
      ↓
現在設定との差分判定
      ↓
VoiceMeeter A2/A3更新
      ↓
Audio Engine Restart
```

### Phase 5: WPF / タスクトレイ

最後に以下を追加する。

-   タスクトレイ常駐
-   現在状態表示
-   設定画面
-   手動 Restart
-   Windows 自動起動
-   ログ

## 20. MVP 対象外

初期バージョンでは以下を対象外とする。

-   VoiceMeeter Potato 対応
-   A1 の自動管理
-   Bluetooth ペアリング操作
-   Windows 標準出力デバイス自体の変更
-   デバイスごとの音量自動調整
-   デバイスごとの EQ
-   複雑なルーティングルール
-   ネットワーク経由での制御
-   自動アップデート

必要になった場合に後から追加できる構造にはしておく。
