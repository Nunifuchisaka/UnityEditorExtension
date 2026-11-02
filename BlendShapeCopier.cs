#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System;

namespace Nunifuchisaka
{
  public class BlendShapeCopier : EditorWindow
  {
    private GameObject sourceObject;
    private GameObject destinationObject;

    [MenuItem("Tools/Nunifuchisaka/BlendShape Copier...", false, 100)]
    public static void ShowWindow()
    {
      GetWindow<BlendShapeCopier>("BlendShape Copier");
    }

    private void OnGUI()
    {
      GUILayout.Label("Copy BlendShape Weights", EditorStyles.boldLabel);
      DocumentationLink.Draw();

      sourceObject = (GameObject)EditorGUILayout.ObjectField("From", sourceObject, typeof(GameObject), true);
      destinationObject = (GameObject)EditorGUILayout.ObjectField("To", destinationObject, typeof(GameObject), true);

      GUILayout.Space(20);

      if (GUILayout.Button("Copy BlendShapes"))
      {
        ExecuteCopy(sourceObject, destinationObject);
      }
    }

    public static void ExecuteCopy(GameObject source, GameObject dest)
    {
      if (!AvatarCopier.ValidateSourceAndDestination(source, dest, "[BlendShapeCopier]")) return;

      Undo.SetCurrentGroupName("Copy BlendShapes");
      int group = Undo.GetCurrentGroup();

      try
      {
        int count = CopyBlendShapesRecursively(source.transform, dest.transform);
        Debug.Log($"[BlendShapeCopier] Complete. {count} 個のSkinnedMeshRendererのBlendShapeウェイトをコピーしました。");
      }
      catch (Exception e)
      {
        Debug.LogError($"[BlendShapeCopier] An error occurred: {e.Message}\n{e.StackTrace}");
      }
      finally
      {
        Undo.CollapseUndoOperations(group);
      }
    }

    private static int CopyBlendShapesRecursively(Transform source, Transform dest)
    {
      int count = 0;
      var sourceSmr = source.GetComponent<SkinnedMeshRenderer>();
      var destSmr = dest.GetComponent<SkinnedMeshRenderer>();

      if (sourceSmr != null && destSmr != null)
      {
        if (sourceSmr.sharedMesh != null && destSmr.sharedMesh != null)
        {
          Undo.RecordObject(destSmr, "Copy BlendShape Weights");

          for (int i = 0; i < sourceSmr.sharedMesh.blendShapeCount; i++)
          {
            string blendShapeName = sourceSmr.sharedMesh.GetBlendShapeName(i);
            int destIndex = destSmr.sharedMesh.GetBlendShapeIndex(blendShapeName);

            if (destIndex != -1)
            {
              float weight = sourceSmr.GetBlendShapeWeight(i);
              destSmr.SetBlendShapeWeight(destIndex, weight);
            }
          }
          count++;
        }
      }

      foreach (Transform sourceChild in source)
      {
        Transform destChild = dest.Find(sourceChild.name);
        if (destChild != null)
        {
          count += CopyBlendShapesRecursively(sourceChild, destChild);
        }
      }
      return count;
    }
  }
}
#endif