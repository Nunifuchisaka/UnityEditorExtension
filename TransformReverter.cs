#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

namespace Nunifuchisaka
{
  public class TransformReverter : EditorWindow
  {
    private GameObject targetObject;
    private bool revertPosition = true;
    private bool revertRotation = true;
    private bool revertScale = true;

    [MenuItem("Tools/Nunifuchisaka/Transform Reverter...", false, 200)]
    public static void ShowWindow()
    {
      GetWindow<TransformReverter>("Transform Reverter");
    }

    private void OnGUI()
    {
      GUIStyle boldLabel = new GUIStyle(EditorStyles.label) { fontStyle = FontStyle.Bold };

      // 1. オブジェクト選択フィールド
      GUILayout.Label("Select Target GameObject", boldLabel);
      DocumentationLink.Draw();
      targetObject = (GameObject)EditorGUILayout.ObjectField("Root Object", targetObject, typeof(GameObject), true);

      EditorGUILayout.Space(10);

      // 2. Revert対象プロパティ選択のチェックボックス
      GUILayout.Label("Choose Properties to Revert", boldLabel);
      revertPosition = EditorGUILayout.Toggle("Revert Position", revertPosition);
      revertRotation = EditorGUILayout.Toggle("Revert Rotation", revertRotation);
      revertScale = EditorGUILayout.Toggle("Revert Scale", revertScale);

      EditorGUILayout.Space(20);

      // 3. 実行ボタン
      GUI.enabled = targetObject != null;
      if (GUILayout.Button("Revert Selected Properties", GUILayout.Height(30)))
      {
        RevertTransforms();
      }
      GUI.enabled = true;
    }

    private void RevertTransforms()
    {
      if (targetObject == null)
      {
        Debug.LogError("[TransformReverter] Target object is not selected.");
        return;
      }
      if (!revertPosition && !revertRotation && !revertScale)
      {
        Debug.LogWarning("[TransformReverter] Revertするプロパティを少なくとも1つチェックしてください。");
        return;
      }

      Undo.SetCurrentGroupName("Revert Transform Properties");
      int group = Undo.GetCurrentGroup();
      int revertedCount = 0;
      try
      {
        // 指定したRoot Object配下のTransformだけを対象にする。
        // （PrefabUtility.GetPropertyModifications はPrefabインスタンス全体のオーバーライドを返すため使わない）
        foreach (Transform target in targetObject.GetComponentsInChildren<Transform>(true))
        {
          if (!PrefabUtility.IsPartOfPrefabInstance(target)) continue;

          var serializedObject = new SerializedObject(target);
          bool reverted = false;
          if (revertPosition) reverted |= RevertIfOverridden(serializedObject, "m_LocalPosition");
          if (revertRotation)
          {
            reverted |= RevertIfOverridden(serializedObject, "m_LocalRotation");
            reverted |= RevertIfOverridden(serializedObject, "m_LocalEulerAnglesHint");
          }
          if (revertScale) reverted |= RevertIfOverridden(serializedObject, "m_LocalScale");
          if (reverted) revertedCount++;
        }
      }
      catch (System.Exception e)
      {
        Debug.LogError($"[TransformReverter] An error occurred: {e.Message}\n{e.StackTrace}");
      }
      finally
      {
        Undo.CollapseUndoOperations(group);
      }

      Debug.Log($"[TransformReverter] Process completed for '{targetObject.name}'. {revertedCount} 個のTransformのオーバーライドを元に戻しました。");
    }

    private static bool RevertIfOverridden(SerializedObject serializedObject, string propertyPath)
    {
      SerializedProperty property = serializedObject.FindProperty(propertyPath);
      if (property == null || !property.prefabOverride) return false;
      // UserAction でUndoに記録される。Vector3などの親プロパティを指定すると子（x/y/z）もまとめて戻る
      PrefabUtility.RevertPropertyOverride(property, InteractionMode.UserAction);
      serializedObject.Update();
      return true;
    }
  }
}
#endif