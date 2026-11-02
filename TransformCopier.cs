#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System;

namespace Nunifuchisaka
{
  public struct TransformCopySettings
  {
    public bool positionX, positionY, positionZ;
    public bool rotationX, rotationY, rotationZ;
    public bool scaleX, scaleY, scaleZ;
  }

  public class TransformCopier : EditorWindow
  {
    private GameObject sourceObject;
    private GameObject destinationObject;

    private TransformCopySettings copySettings;

    private void OnEnable()
    {
      copySettings = new TransformCopySettings
      {
        positionX = true, positionY = true, positionZ = true,
        rotationX = true, rotationY = true, rotationZ = true,
        scaleX = true, scaleY = true, scaleZ = true
      };
    }

    [MenuItem("Tools/Nunifuchisaka/Transform Copier...", false, 100)]
    public static void ShowWindow()
    {
      GetWindow<TransformCopier>("Transform Copier");
    }

    private void OnGUI()
    {
      GUILayout.Label("Copy Transform Hierarchy", EditorStyles.boldLabel);
      DocumentationLink.Draw();

      sourceObject = (GameObject)EditorGUILayout.ObjectField("From", sourceObject, typeof(GameObject), true);
      destinationObject = (GameObject)EditorGUILayout.ObjectField("To", destinationObject, typeof(GameObject), true);

      GUILayout.Space(10);
      
      EditorGUILayout.LabelField("Properties to Copy", EditorStyles.boldLabel);

      // Position
      EditorGUILayout.BeginHorizontal();
      EditorGUILayout.LabelField("Position", GUILayout.Width(70));
      copySettings.positionX = EditorGUILayout.ToggleLeft("X", copySettings.positionX, GUILayout.Width(40));
      copySettings.positionY = EditorGUILayout.ToggleLeft("Y", copySettings.positionY, GUILayout.Width(40));
      copySettings.positionZ = EditorGUILayout.ToggleLeft("Z", copySettings.positionZ, GUILayout.Width(40));
      EditorGUILayout.EndHorizontal();

      // Rotation
      EditorGUILayout.BeginHorizontal();
      EditorGUILayout.LabelField("Rotation", GUILayout.Width(70));
      copySettings.rotationX = EditorGUILayout.ToggleLeft("X", copySettings.rotationX, GUILayout.Width(40));
      copySettings.rotationY = EditorGUILayout.ToggleLeft("Y", copySettings.rotationY, GUILayout.Width(40));
      copySettings.rotationZ = EditorGUILayout.ToggleLeft("Z", copySettings.rotationZ, GUILayout.Width(40));
      EditorGUILayout.EndHorizontal();

      // Scale (Zoom)
      EditorGUILayout.BeginHorizontal();
      EditorGUILayout.LabelField("Scale", GUILayout.Width(70));
      copySettings.scaleX = EditorGUILayout.ToggleLeft("X", copySettings.scaleX, GUILayout.Width(40));
      copySettings.scaleY = EditorGUILayout.ToggleLeft("Y", copySettings.scaleY, GUILayout.Width(40));
      copySettings.scaleZ = EditorGUILayout.ToggleLeft("Z", copySettings.scaleZ, GUILayout.Width(40));
      EditorGUILayout.EndHorizontal();

      GUILayout.Space(20);

      if (GUILayout.Button("Copy Transforms"))
      {
        ExecuteCopy(sourceObject, destinationObject, copySettings);
      }
    }

    public static void ExecuteCopy(GameObject source, GameObject dest, TransformCopySettings? settings = null)
    {
      if (!AvatarCopier.ValidateSourceAndDestination(source, dest, "[TransformCopier]")) return;

      // settingsがnull（未指定）の場合、全てtrueのデフォルト設定を使用する
      TransformCopySettings finalSettings = settings ?? new TransformCopySettings
      {
        positionX = true, positionY = true, positionZ = true,
        rotationX = true, rotationY = true, rotationZ = true,
        scaleX = true, scaleY = true, scaleZ = true
      };

      Undo.SetCurrentGroupName("Copy Transform Hierarchy");
      int group = Undo.GetCurrentGroup();

      try
      {
        CopyTransformsRecursively(source.transform, dest.transform, finalSettings);
        Debug.Log("[TransformCopier] Complete.");
      }
      catch (Exception e)
      {
        Debug.LogError($"[TransformCopier] An error occurred: {e.Message}\n{e.StackTrace}");
      }
      finally
      {
        Undo.CollapseUndoOperations(group);
      }
    }
    
    private static void CopyTransformsRecursively(Transform source, Transform dest, TransformCopySettings settings)
    {
      foreach (Transform sourceChild in source)
      {
        Transform destChild = dest.Find(sourceChild.name);
        
        if (destChild != null)
        {
          Undo.RecordObject(destChild, "Copy Transform");

          Vector3 newPosition = destChild.localPosition;
          if (settings.positionX) newPosition.x = sourceChild.localPosition.x;
          if (settings.positionY) newPosition.y = sourceChild.localPosition.y;
          if (settings.positionZ) newPosition.z = sourceChild.localPosition.z;
          destChild.localPosition = newPosition;

          if (settings.rotationX && settings.rotationY && settings.rotationZ)
          {
            // 全軸コピーはQuaternionをそのまま代入する。
            // オイラー角を経由すると浮動小数点の誤差で値がわずかにずれ、同じ値でもPrefabオーバーライドが付いてしまうため。
            destChild.localRotation = sourceChild.localRotation;
          }
          else if (settings.rotationX || settings.rotationY || settings.rotationZ)
          {
            Vector3 sourceEuler = sourceChild.localEulerAngles;
            Vector3 newEuler = destChild.localEulerAngles;
            if (settings.rotationX) newEuler.x = sourceEuler.x;
            if (settings.rotationY) newEuler.y = sourceEuler.y;
            if (settings.rotationZ) newEuler.z = sourceEuler.z;
            Quaternion newRotation = Quaternion.Euler(newEuler);
            // 実質的に変化がない場合は代入しない（誤差だけのPrefabオーバーライドを防ぐ。Quaternionの==は近似比較）
            if (newRotation != destChild.localRotation) destChild.localRotation = newRotation;
          }

          Vector3 newScale = destChild.localScale;
          if (settings.scaleX) newScale.x = sourceChild.localScale.x;
          if (settings.scaleY) newScale.y = sourceChild.localScale.y;
          if (settings.scaleZ) newScale.z = sourceChild.localScale.z;
          destChild.localScale = newScale;

          CopyTransformsRecursively(sourceChild, destChild, settings);
        }
      }
    }
  }
}
#endif