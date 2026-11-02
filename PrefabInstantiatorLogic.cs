#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

namespace Nunifuchisaka
{
  public static class PrefabInstantiatorLogic
  {
    public static void DuplicatePrefabs(GameObject source, GameObject destination)
    {
      if (!AvatarCopier.ValidateSourceAndDestination(source, destination, "[PrefabInstantiator]")) return;

      Undo.SetCurrentGroupName("Duplicate Prefab Instances");
      int undoGroup = Undo.GetCurrentGroup();

      int duplicatedCount = TraverseAndDuplicate(source.transform, destination.transform);

      Undo.CollapseUndoOperations(undoGroup);

      if (duplicatedCount > 0)
      {
        Debug.Log($"[PrefabInstantiator] {duplicatedCount}個のPrefabインスタンスを '{destination.name}' の階層に複製しました。", destination);
      }
      else
      {
        Debug.Log($"[PrefabInstantiator] 複製対象のPrefabインスタンスが見つからないか、既にすべて存在していました。");
      }
    }

    private static int TraverseAndDuplicate(Transform sourceParent, Transform destinationParent)
    {
      int count = 0;
      for (int i = 0; i < sourceParent.childCount; i++)
      {
        Transform sourceChild = sourceParent.GetChild(i);
        GameObject sourceChildGo = sourceChild.gameObject;

        if (PrefabUtility.IsAnyPrefabInstanceRoot(sourceChildGo))
        {
          if (destinationParent.Find(sourceChild.name) == null)
          {
            string assetPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(sourceChildGo);
            if (string.IsNullOrEmpty(assetPath)) continue;

            GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefabAsset == null) continue;

            GameObject newInstance = (GameObject)PrefabUtility.InstantiatePrefab(prefabAsset, destinationParent);

            if (newInstance != null)
            {
              newInstance.transform.localPosition = sourceChild.localPosition;
              newInstance.transform.localRotation = sourceChild.localRotation;
              newInstance.transform.localScale = sourceChild.localScale;
              newInstance.name = sourceChild.name;

              Undo.RegisterCreatedObjectUndo(newInstance, "Duplicate Prefab Instance");
              count++;
            }
          }
        }
        else
        {
          Transform existingContainer = destinationParent.Find(sourceChild.name);
          if (existingContainer != null)
          {
              count += TraverseAndDuplicate(sourceChild, existingContainer);
          }
        }
      }
      return count;
    }
  }
}
#endif