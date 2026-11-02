#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace Nunifuchisaka
{
  public class ComponentRemover : EditorWindow
  {
    private GameObject rootObject;

    private bool removeAllComponents = false;
    private bool removeVrcComponents = true;
    private bool removeMaComponents = true;
    private bool removeAaoComponents = true;
    private bool removeFloorAdjuster = true;

    [MenuItem("Tools/Nunifuchisaka/Component Remover...", false, 200)]
    public static void ShowWindow()
    {
      GetWindow<ComponentRemover>("Component Remover");
    }

    private void OnGUI()
    {
      GUILayout.Label("Select Target GameObject", EditorStyles.boldLabel);
      DocumentationLink.Draw();

      rootObject = (GameObject)EditorGUILayout.ObjectField("Root Object", rootObject, typeof(GameObject), true);

      GUILayout.Space(10);

      EditorGUILayout.LabelField("Remove Conditions", EditorStyles.boldLabel);
      removeAllComponents = EditorGUILayout.Toggle(new GUIContent("All Components", "Transform・Renderer・MeshFilter・Animatorを除くすべてのコンポーネント（自作ギミックの独自コンポーネントを含む）を削除します。Component Copier の All Components モードでコピーしたものを一括削除するときに使います。"), removeAllComponents);
      EditorGUI.BeginDisabledGroup(removeAllComponents);
      removeVrcComponents = EditorGUILayout.Toggle(new GUIContent("VRC", "VRChat SDK関連のコンポーネント（VRC...で始まるもの）を削除します。"), removeVrcComponents);
      removeMaComponents = EditorGUILayout.Toggle(new GUIContent("ModularAvatar", "Modular Avatar関連のコンポーネント（ModularAvatar...で始まるもの）を削除します。"), removeMaComponents);
      removeAaoComponents = EditorGUILayout.Toggle(new GUIContent("AAO TraceAndOptimize", "Avatar OptimizerのTraceAndOptimizeを削除します。"), removeAaoComponents);
      removeFloorAdjuster = EditorGUILayout.Toggle(new GUIContent("FloorAdjuster", "FloorAdjusterコンポーネントを削除します。"), removeFloorAdjuster);
      EditorGUI.EndDisabledGroup();

      GUILayout.Space(20);

      if (GUILayout.Button("Remove"))
      {
        RemoveSelectedComponents();
      }
    }

    private void RemoveSelectedComponents()
    {
      if (rootObject == null)
      {
        Debug.LogError("[ComponentRemover] Root Object を設定してください。");
        return;
      }

      if (!removeAllComponents && !removeVrcComponents && !removeMaComponents && !removeAaoComponents && !removeFloorAdjuster)
      {
        Debug.LogWarning("[ComponentRemover] 削除する条件を少なくとも1つチェックしてください。");
        return;
      }

      List<Component> componentsToRemove = CollectTargets();
      if (componentsToRemove.Count == 0)
      {
        Debug.LogWarning("[ComponentRemover] No matching components found to remove in the hierarchy.");
        return;
      }

      if (!EditorUtility.DisplayDialog(
            "Remove の確認",
            $"'{rootObject.name}' の階層から {componentsToRemove.Count} 個のコンポーネントを削除します（Undoで戻せます）。\n\n実行しますか？",
            "削除", "キャンセル"))
      {
        return;
      }

      Undo.SetCurrentGroupName("Remove Components from " + rootObject.name + " Hierarchy");
      int undoGroup = Undo.GetCurrentGroup();

      try
      {
        int removedCount = RemoveRespectingDependencies(componentsToRemove);
        Debug.Log($"[ComponentRemover] ----- Finished: Removed {removedCount} component(s) successfully from the hierarchy. -----");
      }
      catch (System.Exception e)
      {
        Debug.LogError($"[ComponentRemover] An error occurred: {e.Message}\n{e.StackTrace}");
      }
      finally
      {
        Undo.CollapseUndoOperations(undoGroup);
      }
    }

    private List<Component> CollectTargets()
    {
      var targets = new List<Component>();
      foreach (var component in rootObject.GetComponentsInChildren<Component>(true))
      {
        if (component == null || component is Transform) continue;

        bool isTarget = removeAllComponents
          // Animatorはアバターのルートに必須なため、All Componentsでも削除しない
          ? ComponentFilter.IsCopyTarget(component, false, false, false, false, true) && !(component is Animator)
          : ComponentFilter.Matches(component, removeVrcComponents, removeMaComponents, removeAaoComponents, removeFloorAdjuster);
        if (isTarget) targets.Add(component);
      }
      return targets;
    }

    /// <summary>
    /// [RequireComponent] で他のコンポーネントから必要とされているものは削除に失敗するため、
    /// 必要とされていないものから順に削除する。最後まで削除できなかったものは警告を出す。
    /// </summary>
    private static int RemoveRespectingDependencies(List<Component> targets)
    {
      var remaining = new List<Component>(targets);
      int removedCount = 0;
      bool progressed = true;
      while (remaining.Count > 0 && progressed)
      {
        progressed = false;
        for (int i = 0; i < remaining.Count; i++)
        {
          Component component = remaining[i];
          if (component == null)
          {
            remaining.RemoveAt(i--);
            continue;
          }
          if (IsRequiredByOtherComponent(component)) continue;

          string componentNameForLog = component.GetType().Name;
          string gameObjectNameForLog = component.gameObject.name;
          Undo.DestroyObjectImmediate(component);
          Debug.Log($"[ComponentRemover] Removed component '{componentNameForLog}' from '{gameObjectNameForLog}'.");

          remaining.RemoveAt(i--);
          removedCount++;
          progressed = true;
        }
      }

      foreach (var component in remaining)
      {
        if (component == null) continue;
        Debug.LogWarning($"[ComponentRemover] '{component.gameObject.name}' の '{component.GetType().Name}' は、削除対象外のコンポーネントから必要とされているため削除できませんでした。", component);
      }
      return removedCount;
    }

    private static bool IsRequiredByOtherComponent(Component target)
    {
      Component[] siblings = target.GetComponents<Component>();
      foreach (var other in siblings)
      {
        if (other == null || other == target) continue;
        foreach (RequireComponent requirement in other.GetType().GetCustomAttributes(typeof(RequireComponent), true))
        {
          if (BlocksRemoval(requirement.m_Type0, target, siblings) ||
              BlocksRemoval(requirement.m_Type1, target, siblings) ||
              BlocksRemoval(requirement.m_Type2, target, siblings))
          {
            return true;
          }
        }
      }
      return false;
    }

    // requiredType を満たすのが target だけなら、target は削除できない
    private static bool BlocksRemoval(System.Type requiredType, Component target, Component[] siblings)
    {
      if (requiredType == null || !requiredType.IsInstanceOfType(target)) return false;
      foreach (var sibling in siblings)
      {
        if (sibling != null && sibling != target && requiredType.IsInstanceOfType(sibling)) return false;
      }
      return true;
    }
  }
}
#endif
