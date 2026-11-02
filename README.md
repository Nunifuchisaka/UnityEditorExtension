# UnityEditorExtention

VRChatアバター向けのUnityエディタ拡張集です。アバターの衣装替えや素体の差し替えの際に、旧アバターから新アバターへセットアップ（PhysBone・Modular Avatar・マテリアル・BlendShapeなど）を移行する作業を自動化します。

## 動作環境

- Unity 2022.3 系（VRChat推奨バージョン）で動作確認
- VRChat SDK / Modular Avatar / AAO (Avatar Optimizer) / FloorAdjuster へのアセンブリ参照は持たず、型名の文字列マッチで判定します。これらのパッケージが入っていないプロジェクトでもコンパイルエラーにはなりません

## 導入方法

### VCC（VRChat Creator Companion）から（推奨）

1. VCCの `Settings > Packages > Add Repository` に以下のURLを追加する

   ```
   https://nunifuchisaka.github.io/UnityEditorExtension/index.json
   ```

   （リスティングのWebページ <https://nunifuchisaka.github.io/UnityEditorExtension/> の「Add to VCC」ボタンからも追加できます）
2. VCCでプロジェクトの `Manage Project` を開き、**Nunifuchisaka Avatar Tools** を追加する

導入後、メニューの `Tools > Nunifuchisaka` から各ツールを開けます。

### 手動で（VCCを使わない場合）

このリポジトリの内容を、Unityプロジェクトの `Assets` 配下の `Editor` フォルダ（例：`Assets/Nunifuchisaka/Editor/`）に配置してください。VCCからインストールし直す場合は、手動配置したフォルダを先に削除してください。

## 主な使い方：アバターの移行

1. **Tools > Nunifuchisaka > Avatar Copier...** を開く
2. `From` に移行元アバター、`To` に移行先アバターを指定する
3. **Copy All** を押すと、以下を順番に実行します
   1. Prefabインスタンスの複製（`From` 直下のPrefabを `To` に複製）
   2. Transformのコピー（同名オブジェクト同士）
   3. マテリアルのコピー
   4. BlendShapeウェイトのコピー
   5. アクティブ状態の同期
   6. コンポーネントのコピー（デフォルトの「All Components」では自作ギミックの独自コンポーネントも含むすべて。階層の複製と参照のリマップを含む）
4. **Compare (移行結果の確認)** を押して Avatar Comparer で移行漏れがないか確認する

各ステップは個別のボタンでも実行できます。シーンを変更する操作はすべてUndo（Ctrl+Z）で戻せます。

## ツール一覧

| メニュー | 説明 |
| --- | --- |
| Avatar Copier... | アバター移行のオーケストレーター。上記の全ステップを一括または個別に実行。Copy All の前に同名の兄弟オブジェクトをチェックし、見つかった場合は続行するか確認する。「Revert All 'To Object'」は確認ダイアログの後に実行 |
| Avatar Comparer... | 移行元と移行先を比較し、移行漏れや差異を検出。階層・Transform・Material・BlendShape・アクティブ状態・コンポーネント数・参照のリマップ漏れをエラー／警告で一覧表示し、`Select` で該当オブジェクトにジャンプできる。「All Components」（デフォルトON）では Transform・Renderer・MeshFilter 以外のすべてのコンポーネントを比較し、Unity標準コンポーネント（Constraint・Joint など）の参照も検査する。OFFにすると VRC / Modular Avatar / AAO / FloorAdjuster のみを比較 |
| Component Copier... | コンポーネントを階層ごとコピー。3パス構成（①階層の複製 → ②コンポーネントのコピー → ③コピー元階層への参照をコピー先の対応オブジェクトへリマップ。VRC・MA・自作ギミックなどのスクリプトに加え、Unity標準コンポーネントの参照もリマップする）。「All Components」（デフォルトON）では Transform・Renderer・MeshFilter を除くすべてのコンポーネントをコピーし、コピー先に存在しないオブジェクトは子孫ごと丸ごと複製する（Renderer・マテリアルも含む）。OFFにすると VRC / Modular Avatar / AAO / FloorAdjuster から選んでコピー。「Keep Dest Blueprint ID」（デフォルトON）では、コピー先の PipelineManager に既にある Blueprint ID を上書きしない（空なら移行元のIDを引き継ぐ） |
| Component Remover... | 指定した階層から VRC / Modular Avatar / AAO / FloorAdjuster のコンポーネントを一括削除。「All Components」では Transform・Renderer・MeshFilter・Animator 以外のすべて（自作ギミックの独自コンポーネントを含む）を削除する。削除前に件数を確認し、`[RequireComponent]` で依存されているものは依存元から順に削除する |
| Component Viewer... | 選択中オブジェクトのコンポーネント名を一覧表示し、クリップボードにコピー |
| Prefab Instantiator... | `From` 直下のPrefabインスタンスを `To` の階層に複製 |
| Transform Copier... | 同名オブジェクト同士でTransformをコピー（位置・回転・スケールを軸別に選択可能） |
| BlendShape Copier... | 同名オブジェクト・同名BlendShape同士でウェイトをコピー |
| Sync ActiveState... | 同名オブジェクト同士でアクティブ状態を同期（ルートオブジェクト自身は同期しない。移行元を非表示にして作業していても移行先は非表示にならない） |
| Transform Reverter... | 指定した Root Object 配下の Transform のオーバーライドだけを、位置・回転・スケール別に選択的にRevert（Root Object の外は変更しない） |
| Import Package with Preview... | `.unitypackage` の内容をインポート前にプレビューし、既存アセットとの競合を確認。同じGUIDのアセットは `Overwrite`、同じパスに別のアセットがあり別名でインポートされるものは `Rename` と表示する。大きなパッケージでもメモリに全展開せずに読み込む |
| Booth Package Exporter... | パッケージ資産を `.unitypackage` 化し、README・LICENSE・利用規約とともにBOOTH配布用zipにまとめる（開発者用） |

## 注意事項

- オブジェクトの対応付けは**同名の子オブジェクト**で行います。同じ階層に同名の兄弟オブジェクトがあると正しく対応付けられません（Copy All の実行前と Avatar Comparer で警告します）
- From と To に同じオブジェクトや親子関係にあるオブジェクトは指定できません
- ルートオブジェクト自身の Transform とアクティブ状態はコピー・比較の対象外です
- Material Copier は、サブメッシュ数が異なる Renderer やマテリアルが不足するサブメッシュがある Renderer を Console に警告します
- 「All Components」をOFFにしたときのコンポーネントの判定は型名・名前空間の文字列マッチです（`VRC...`、`ModularAvatar...`、`TraceAndOptimize`、`FloorAdjuster`）
- コピー先に既にある Animator は上書きしません（素体を差し替えたときに、移行元の Humanoid Avatar アセットでボーンの対応が壊れるのを防ぐため）
- 「All Components」でコピー先に存在しないオブジェクトを丸ごと複製したとき、その中にあるPrefabインスタンスはPrefabとの接続が切れた通常のオブジェクトになります
- Avatar Comparer の比較結果はウィンドウのほかConsoleにも出力されます
- 各ツールのウィンドウ上部に「ドキュメントを開く」リンクがあり、このREADMEをブラウザで開けます
