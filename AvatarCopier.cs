#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

namespace Nunifuchisaka
{
  public class AvatarCopier : EditorWindow
  {
    private GameObject sourceObject;
    private GameObject destinationObject;

    private bool copyAllComponents = true;
    private bool copyVrcComponents = true;
    private bool copyMaComponents = true;
    private bool copyAaoComponents = true;
    private bool copyFloorAdjuster = true;
    private bool keepDestBlueprintId = true;

    [MenuItem("Tools/Nunifuchisaka/Avatar Copier...", false, 1)]
    public static void ShowWindow()
    {
      GetWindow<AvatarCopier>("Avatar Copier");
    }

    /// <summary>
    /// From/To の組み合わせを検証する（未指定・同一オブジェクト・親子関係）。各ツールの Execute から共通で呼ぶ。
    /// </summary>
    public static bool ValidateSourceAndDestination(GameObject source, GameObject dest, string logPrefix)
    {
      if (source == null || dest == null)
      {
        Debug.LogError($"{logPrefix} From と To の両方のオブジェクトを指定してください。");
        return false;
      }
      if (source == dest)
      {
        Debug.LogError($"{logPrefix} From と To に同じオブジェクトが指定されています。");
        return false;
      }
      if (dest.transform.IsChildOf(source.transform) || source.transform.IsChildOf(dest.transform))
      {
        Debug.LogError($"{logPrefix} From と To が親子関係にあります。別々のオブジェクトを指定してください。");
        return false;
      }
      return true;
    }

    /// <summary>
    /// Copy All の実行前チェック。同名の兄弟オブジェクトがある場合は対応付けが崩れるため、続行するか確認する。
    /// </summary>
    private bool ConfirmCopyAll()
    {
      if (!ValidateSourceAndDestination(sourceObject, destinationObject, "[AvatarCopier]")) return false;

      var duplicates = new List<string>();
      foreach (var path in AvatarComparer.FindDuplicateSiblingPaths(sourceObject)) duplicates.Add("From: " + path);
      foreach (var path in AvatarComparer.FindDuplicateSiblingPaths(destinationObject)) duplicates.Add("To: " + path);
      if (duplicates.Count == 0) return true;

      foreach (var duplicate in duplicates)
      {
        Debug.LogWarning($"[AvatarCopier] 同名の兄弟オブジェクトがあります: {duplicate}");
      }
      const int MaxListed = 10;
      string list = string.Join("\n", duplicates.GetRange(0, Mathf.Min(duplicates.Count, MaxListed)));
      if (duplicates.Count > MaxListed) list += $"\n...ほか {duplicates.Count - MaxListed} 件（Consoleを参照）";
      return EditorUtility.DisplayDialog(
        "同名の兄弟オブジェクトがあります",
        "以下のオブジェクトは同じ階層に同名の兄弟があるため、正しく対応付けできません（1つ目にだけコピーされます）。\n\n" + list + "\n\n続行しますか？",
        "続行", "キャンセル");
    }

    private void OnGUI()
    {
      GUILayout.Label("アバター総合コピー", EditorStyles.boldLabel);
      DocumentationLink.Draw();
      EditorGUILayout.Space(10);

      EditorGUILayout.BeginVertical(GUI.skin.box);
      GUILayout.Label("対象オブジェクトの指定", EditorStyles.boldLabel);
      EditorGUILayout.Space(5);

      sourceObject = (GameObject)EditorGUILayout.ObjectField("From", sourceObject, typeof(GameObject), true);
      EditorGUILayout.Space(5);
      destinationObject = (GameObject)EditorGUILayout.ObjectField("To", destinationObject, typeof(GameObject), true);

      EditorGUILayout.EndVertical();

      EditorGUILayout.Space(10);

      bool canProcess = sourceObject != null && destinationObject != null;
      EditorGUI.BeginDisabledGroup(!canProcess);
      {
        EditorGUILayout.BeginVertical(GUI.skin.box);
        GUILayout.Label("すべての機能を順番に実行", EditorStyles.boldLabel);
        EditorGUILayout.Space(5);
        if (GUILayout.Button("Copy All") && ConfirmCopyAll())
        {
          // 全ステップをひとつのUndoグループにまとめ、Ctrl+Z一発で戻せるようにする
          Undo.IncrementCurrentGroup();
          Undo.SetCurrentGroupName("Avatar Copy All");
          int undoGroup = Undo.GetCurrentGroup();

          PrefabInstantiatorLogic.DuplicatePrefabs(sourceObject, destinationObject);
          TransformCopier.ExecuteCopy(sourceObject, destinationObject);
          MaterialCopier.ExecuteCopy(sourceObject, destinationObject);
          BlendShapeCopier.ExecuteCopy(sourceObject, destinationObject);
          SyncActiveState.Execute(sourceObject, destinationObject);
          ComponentCopier.ExecuteCopy(sourceObject, destinationObject, copyVrcComponents, copyMaComponents, copyAaoComponents, copyFloorAdjuster, copyAllComponents, keepDestBlueprintId);

          Undo.CollapseUndoOperations(undoGroup);
        }
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(10);

        EditorGUILayout.BeginVertical(GUI.skin.box);
        GUILayout.Label("機能ごとに個別で実行", EditorStyles.boldLabel);
        EditorGUILayout.Space(5);

        // Prefabインスタンスをコピー
        if (GUILayout.Button("Copy Prefab Instance"))
        {
          PrefabInstantiatorLogic.DuplicatePrefabs(sourceObject, destinationObject);
        }

        EditorGUILayout.Space(10);

        // Transformをコピー
        if (GUILayout.Button("Copy Transform"))
        {
          TransformCopier.ExecuteCopy(sourceObject, destinationObject);
        }

        EditorGUILayout.Space(10);

        // Materialをコピー
        if (GUILayout.Button("Copy Material"))
        {
          MaterialCopier.ExecuteCopy(sourceObject, destinationObject);
        }

        EditorGUILayout.Space(10);

        // BlendShapeをコピー
        if (GUILayout.Button("Copy BlendShape"))
        {
          BlendShapeCopier.ExecuteCopy(sourceObject, destinationObject);
        }

        EditorGUILayout.Space(10);

        // Active状態を同期
        if (GUILayout.Button("Sync ActiveState"))
        {
          SyncActiveState.Execute(sourceObject, destinationObject);
        }

        EditorGUILayout.Space(10);

        // Componentをコピー
        EditorGUILayout.LabelField("Copy Component", EditorStyles.boldLabel);
        copyAllComponents = EditorGUILayout.Toggle(new GUIContent("All Components", "Transform・Renderer・MeshFilterを除くすべてのコンポーネントをコピーします（既存のAnimatorは上書きしません）。コピー先に存在しないオブジェクトは子孫ごと丸ごと複製します。"), copyAllComponents);
        EditorGUI.BeginDisabledGroup(copyAllComponents);
        copyVrcComponents = EditorGUILayout.Toggle(new GUIContent("VRC", "VRChat SDK関連のコンポーネントをコピーします。"), copyVrcComponents);
        copyMaComponents = EditorGUILayout.Toggle(new GUIContent("ModularAvatar", "Modular Avatar関連のコンポーネントをコピーします。"), copyMaComponents);
        copyAaoComponents = EditorGUILayout.Toggle(new GUIContent("AAO TraceAndOptimize", "TraceAndOptimizeコンポーネントをコピーします。"), copyAaoComponents);
        copyFloorAdjuster = EditorGUILayout.Toggle(new GUIContent("FloorAdjuster", "FloorAdjusterコンポーネントをコピーします。"), copyFloorAdjuster);
        EditorGUI.EndDisabledGroup();
        keepDestBlueprintId = EditorGUILayout.Toggle(new GUIContent("Keep Dest Blueprint ID", ComponentCopier.KeepBlueprintIdTooltip), keepDestBlueprintId);
        if (GUILayout.Button("Copy Component"))
        {
          ComponentCopier.ExecuteCopy(sourceObject, destinationObject, copyVrcComponents, copyMaComponents, copyAaoComponents, copyFloorAdjuster, copyAllComponents, keepDestBlueprintId);
        }
        EditorGUILayout.EndVertical();
      }
      EditorGUI.EndDisabledGroup();

      EditorGUILayout.Space(10);
      // GUILayout.Box("", GUILayout.ExpandWidth(true), GUILayout.Height(20));

      // Only destinationObject
      EditorGUILayout.BeginVertical(GUI.skin.box);
      GUILayout.Label("ユーティリティ操作", EditorStyles.boldLabel);
      EditorGUILayout.Space(5);

      EditorGUI.BeginDisabledGroup(!(destinationObject != null));
      {
        if (GUILayout.Button("Revert All 'To Object'") && EditorUtility.DisplayDialog(
              "Revert All の確認",
              $"'{destinationObject.name}' のPrefabインスタンスのオーバーライドをすべて元に戻します。\nコピーで追加したオブジェクトやコンポーネントも削除されます（Undoで戻せます）。\n\n実行しますか？",
              "Revert", "キャンセル"))
        {
          ObjectReverter.RevertAll(destinationObject);
        }
      }
      EditorGUI.EndDisabledGroup();

      EditorGUILayout.Space(10);

      // 移行結果の比較確認（AvatarComparerを開く）
      EditorGUI.BeginDisabledGroup(!canProcess);
      {
        if (GUILayout.Button("Compare (移行結果の確認)"))
        {
          AvatarComparer.ShowWindow(sourceObject, destinationObject, copyAllComponents);
        }
      }
      EditorGUI.EndDisabledGroup();
      EditorGUILayout.EndVertical();
    }
  }
}
#endif