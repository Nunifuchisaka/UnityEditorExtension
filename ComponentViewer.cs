#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Nunifuchisaka
{
  public class ComponentViewer : EditorWindow
  {
    [MenuItem("Tools/Nunifuchisaka/Component Viewer...", false, 200)]
    public static void ShowWindow()
    {
      GetWindow<ComponentViewer>("Component Viewer");
    }

    private void OnGUI()
    {
      DocumentationLink.Draw();

      GameObject selectedObject = Selection.activeGameObject;

      if (selectedObject != null)
      {
        EditorGUILayout.LabelField("Selected Object:", selectedObject.name, EditorStyles.boldLabel);
        
        foreach (Component component in selectedObject.GetComponents<Component>())
        {
          if (component != null)
          {
            EditorGUILayout.BeginHorizontal();
            
            string componentName = component.GetType().Name;
            EditorGUILayout.LabelField("- " + componentName);

            if (GUILayout.Button("Copy", GUILayout.Width(60)))
            {
              EditorGUIUtility.systemCopyBuffer = componentName;
            }
            
            EditorGUILayout.EndHorizontal();
          }
        }
      }
      else
      {
        EditorGUILayout.LabelField("No object selected.");
      }
    }

    private void OnSelectionChange()
    {
      Repaint();
    }
  }
}
#endif