# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 概要

VRChatアバター用のUnityエディタ拡張集。VCCプロジェクト（Avatar3）の `Assets/Nunifuchisaka/Editor/` 配下にあり、このディレクトリ単体がgitリポジトリになっている。ビルド・テストコマンドはない — Unityエディタが自動でコンパイルし、エラーはUnityのConsoleに出る。ツール一覧とユーザー向けの使い方は `README.md` を参照。

## アーキテクチャ

- 各 `.cs` ファイルは独立したツール。基本パターン：`namespace Nunifuchisaka` 内の `EditorWindow` 派生クラスで、`[MenuItem("Tools/Nunifuchisaka/〇〇...", false, priority)]` でメニュー登録する。ファイル全体を `#if UNITY_EDITOR` で囲む。
- 各ツールはUI（`OnGUI`）とロジック（`public static` な `Execute` / `ExecuteCopy` メソッド）を分離している。これは `AvatarCopier` が各ツールのロジックを直接呼び出すため。UIを持たないロジックだけのクラスは `〇〇Logic`（例：`PrefabInstantiatorLogic`）。
- `AvatarCopier` がオーケストレーター。「Copy All」で以下を順に実行する：
  `PrefabInstantiatorLogic.DuplicatePrefabs` → `TransformCopier` → `MaterialCopier` → `BlendShapeCopier` → `SyncActiveState` → `ComponentCopier`
- コピー元とコピー先のオブジェクト対応付けは、全ツール共通で**同名の子オブジェクト**（相対パス）で行う。同じ階層に同名の兄弟がいると正しく対応しない（`AvatarComparer.FindDuplicateSiblingPaths` で検出し、Copy All 前の確認ダイアログと Comparer で警告する）。ルートオブジェクト自身の Transform・アクティブ状態はコピー・比較しない。
- From/To を受け取る `Execute` 系メソッドは、冒頭で `AvatarCopier.ValidateSourceAndDestination`（未指定・同一・親子関係のチェック）を呼ぶ。
- VRChat SDK / Modular Avatar / AAO / FloorAdjuster への参照はアセンブリ参照を持たず、型名・名前空間の文字列マッチで判定する。判定ロジックは `ComponentFilter` に一元化されており、新しい判定もここに追加する（常時除外の `IsAlwaysExcluded`、コピー／比較対象判定の `IsCopyTarget` もここ）。これらのパッケージに直接依存するコードを書かないこと。
- `ComponentCopier` は3パス構成：①階層の複製 → ②コンポーネントのコピー（`GetSortPriority` で PhysBoneCollider → PhysBone の順序を保証。同型複数は出現順のn番目同士で対応付け）→ ③コピー元階層内への参照をコピー先の対応オブジェクトへリマップ。
  - 「All Components」モード（`doCopyAll`）では `ComponentFilter` の種類判定をバイパスしてすべてコピーする。ただし Transform / Renderer / MeshFilter は `ComponentFilter.IsAlwaysExcluded` で常に除外（Transform・Material・BlendShapeは専用ツールの担当のため）。
  - 「All Components」モードの Pass 1 では、コピー先に存在しないオブジェクトを `Object.Instantiate` で子孫ごと丸ごと複製する（除外対象の Renderer / MeshFilter も含む。見た目のない抜け殻にしないため）。複製したルートは `CopyContext.ClonedRoots` に記録し、Pass 2 ではスキップする。複製範囲内のPrefabインスタンスはPrefab接続が切れる。フィルタモードでは従来どおり空のGameObjectを作る。
  - コピー先に既に Animator がある場合は上書きしない（コピー先FBX由来の Humanoid Avatar アセットを保持するため。Avatar Comparer も Animator の中身は比較しない）。
  - `keepDestBlueprintId`（デフォルトtrue）では、コピー先の PipelineManager に既にある `blueprintId` を貼り付け後に書き戻す（`SerializedObject` 経由）。
  - 参照のリマップで同型コンポーネントが複数ある場合は、Pass 2 と同じく出現順のn番目同士で対応付ける。対応するn番目がなければリマップせず残す（Comparer がエラーとして検出する）。
  - Pass 3のリマップは、MonoBehaviour（VRC / MA / 自作ギミック）はC#フィールドのリフレクションで、ネイティブ実装の標準コンポーネント（Constraint・Joint・Cloth・SkinnedMeshRendererのbones など）は `SerializedObject` のObjectReferenceプロパティ走査で行う。`m_GameObject` などの内部プロパティは対象外。
- シーンを変更する操作は必ずUndo対応する：`Undo.SetCurrentGroupName` + `Undo.GetCurrentGroup` で開始し、`Undo.RegisterCreatedObjectUndo` / `Undo.AddComponent` / `Undo.RecordObject` を使い、最後に `Undo.CollapseUndoOperations` でまとめる。リフレクションでフィールドを書き換えた後は `PrefabUtility.RecordPrefabInstancePropertyModifications` + `EditorUtility.SetDirty` で永続化する。

## テスト

- 自動テスト基盤はない。動作テストは、このディレクトリ直下の `Test.unity` シーンで必ず実行する（作業中のシーンを汚さないため）。ユーザーにUnityエディタで `Test.unity` を開いてもらってから実行を依頼する。
- テスト方法：一時的なエディタスクリプト（`TempSelfTest.cs` など）を作成してメニューから実行してもらい、結果を `[SelfTest] PASS/FAIL` 形式で `Debug.Log` に出す。結果は `C:\Users\nunif\AppData\Local\Unity\Editor\Editor.log` から読み取れる（過去セッションのログも残っているのでファイル末尾の実行分を見る）。検証が終わったら一時スクリプトは削除する。
- VRChat SDK等の型はアセンブリ参照せず、テスト内でもリフレクション（型名検索）で取得する。
- コンパイル検証だけならUnityを開かずに可能。Unityバージョンは `ProjectSettings/ProjectVersion.txt` を参照。パスにスペースを含むためレスポンスファイル方式を使う（bash）：

  ```bash
  cd "/c/Users/nunif/Dropbox/3DModel/VCC_Projects/Avatar3/Assets/Nunifuchisaka/Editor"
  U="/c/Program Files/Unity/Hub/Editor/2022.3.22f1/Editor/Data"
  UW="C:/Program Files/Unity/Hub/Editor/2022.3.22f1/Editor/Data"
  RSP="$TMP/check.rsp"
  { echo "-target:library"; echo "-define:UNITY_EDITOR"; echo "-nowarn:0169,0649"
    echo "-out:$TMP/check.dll"
    for f in "$U/Managed/UnityEngine/"*.dll; do echo "-reference:\"$UW/Managed/UnityEngine/$(basename "$f")\""; done
    echo "-reference:\"$UW/NetStandard/ref/2.1.0/netstandard.dll\""
    for f in *.cs; do echo "\"$f\""; done; } > "$RSP"
  "$U/NetCoreRuntime/dotnet.exe" "$U/DotNetSdkRoslyn/csc.dll" -nologo "@$RSP" && echo COMPILE_OK
  ```

## 規約

- インデントはスペース2つ。改行コードはLF（`.gitattributes` で `eol=lf` を指定済み）。
- UIラベル・ログ・コメントは日本語と英語が混在（既存ファイルに合わせる）。ログは `[クラス名]` プレフィックス付きで `Debug.Log` / `LogWarning` / `LogError` を使う。
- `.meta` ファイルはgit管理に**含める**（VPM配布にGUID固定の `.meta` が必須のため）。新規ファイル作成時はUnityが生成した `.meta` をコミットする（原則手動で作らない）。`.cs` をリネームする際はGUID維持のため `.meta` も同名にリネームする。
- ツールの機能を追加・変更したら `README.md` のツール一覧・使い方も更新する。
- コードを変更したら `package.json` の `version` を上げる（パッチ番号のインクリメントでよい）。

## VPMパッケージとリリース

- このリポジトリはVPMパッケージ `com.nunifuchisaka.avatar-tools` そのもの。リポジトリ直下がパッケージルートで、`package.json` がVPMマニフェスト、`Nunifuchisaka.Editor.asmdef` がEditor専用アセンブリ定義。
- リリース手順：`package.json` の `version` を上げてpush → GitHubのActionsで **Build Release** を手動実行（workflow_dispatch）。zipとpackage.jsonがGitHub Releasesに公開され、続けて **Build Repo Listing** が自動実行されてVCC用リスティングがGitHub Pages（`https://nunifuchisaka.github.io/UnityEditorExtension/index.json`）に再生成される。
- `Website/`（リスティングWebページのテンプレート）・`source.json`（リスティング定義）・`Test.unity`・`CLAUDE.md`・`.github/`・`.gitignore`・`.gitattributes`・`BoothPackageExporter.cs` は開発用で、配布zipには含まれない（除外リストは `.github/workflows/release.yml`）。ツールの `.cs` を追加したときは除外リストの更新は不要だが、開発専用ファイルを追加したときは除外リストにも足すこと。

## BOOTH配布

- VCC/VPM経由の配布とは別に、BOOTH（booth.pm）へは手動でzipをアップロードする必要がある。**Tools > Nunifuchisaka > Booth Package Exporter...**（`BoothPackageExporter.cs`、開発者専用ツール）を実行すると、パッケージ資産を `.unitypackage` 化した上で、`README.md`・`LICENSE`・`Terms.txt`（利用規約）と一緒にBOOTH配布用zipにまとめて出力する。
- 出力先は `dist~/`。フォルダ名末尾の `~` によりUnityがアセットとしてインポートしないため `.meta` は生成されず、`.gitignore` でもgit管理から除外している。
- `LICENSE` と `Terms.txt` は開発専用ファイルではないため、VCC/VPM配布zipにも通常どおり含まれる。
