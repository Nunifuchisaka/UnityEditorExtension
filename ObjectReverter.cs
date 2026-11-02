#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Nunifuchisaka
{
  public static class ObjectReverter
  {
    public static void RevertAll(GameObject targetObject)
    {
      if (targetObject == null)
      {
        Debug.LogWarning("[ObjectReverter] 対象のGameObjectがnullです。");
        return;
      }

      GameObject instanceRoot = PrefabUtility.GetNearestPrefabInstanceRoot(targetObject);

      if (instanceRoot == null)
      {
        Debug.LogWarning($"[ObjectReverter] '{targetObject.name}' はPrefabインスタンスの一部ではないため、Revertできません。");
        return;
      }

      EditorApplication.delayCall += () =>
      {
        if (instanceRoot != null)
        {
          // UserActionはUndo登録されるモード（AutomatedActionはUndo不可）
          PrefabUtility.RevertPrefabInstance(instanceRoot, InteractionMode.UserAction);
          Debug.Log($"[ObjectReverter] Complete.");
        }
      };
    }
  }
}
#endif