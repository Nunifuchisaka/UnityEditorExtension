#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

namespace Nunifuchisaka
{
  public class PrefabInstantiator : EditorWindow
  {
    private GameObject sourceObject;
    private GameObject destinationObject;

    [MenuItem("Tools/Nunifuchisaka/Prefab Instantiator...", false, 100)]
    public static void ShowWindow()
    {
      GetWindow<PrefabInstantiator>("Prefab Instantiator");
    }

    private void OnGUI()
    {
      GUILayout.Label("Prefab ユーティリティ", EditorStyles.boldLabel);
      DocumentationLink.Draw();
      EditorGUILayout.Space(10);

      sourceObject = (GameObject)EditorGUILayout.ObjectField("From", sourceObject, typeof(GameObject), true);
      destinationObject = (GameObject)EditorGUILayout.ObjectField("To", destinationObject, typeof(GameObject), true);

      EditorGUILayout.Space(20);

      // 複製ボタン
      bool canProcess = sourceObject != null && destinationObject != null;
      EditorGUI.BeginDisabledGroup(!canProcess);
      {
        if (GUILayout.Button("Prefabインスタンスを複製先にコピー"))
        {
          PrefabInstantiatorLogic.DuplicatePrefabs(sourceObject, destinationObject);
        }
      }
      EditorGUI.EndDisabledGroup();
    }

  }
}
#endif