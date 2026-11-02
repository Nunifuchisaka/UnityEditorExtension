#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Nunifuchisaka
{
  public class SyncActiveState : EditorWindow
  {
    private GameObject sourceObject;
    private GameObject destinationObject;

    // メニューにウィンドウを開く項目を追加
    [MenuItem("Tools/Nunifuchisaka/Sync ActiveState...", false, 100)]
    public static void ShowWindow()
    {
      GetWindow<SyncActiveState>("SyncActiveState");
    }

    /// <summary>
    /// 引数で指定されたオブジェクト間でアクティブ状態を同期します。
    /// </summary>
    public static void Execute(GameObject source, GameObject destination)
    {
      if (!AvatarCopier.ValidateSourceAndDestination(source, destination, "[SyncActiveState]")) return;

      Undo.SetCurrentGroupName("Sync Active State");
      int group = Undo.GetCurrentGroup();

      // ルート同士は同期しない（作業中に移行元アバターを非表示にしていると、移行先まで非表示になってしまうため）
      foreach (Transform sourceChild in source.transform)
      {
        Transform destinationChild = destination.transform.Find(sourceChild.name);
        if (destinationChild != null)
        {
          RecursiveSync(sourceChild, destinationChild);
        }
      }

      Undo.CollapseUndoOperations(group);
      Debug.Log($"[SyncActiveState] '{source.name}' のアクティブ状態を '{destination.name}' に同期しました。");
    }

    // ウィンドウのUIを描画
    private void OnGUI()
    {
      GUILayout.Label("オブジェクトのアクティブ状態を同期", EditorStyles.boldLabel);
      DocumentationLink.Draw();
      EditorGUILayout.HelpBox("Destinationオブジェクトのアクティブ状態を、Sourceオブジェクトの状態に合わせます。\nSourceに存在してDestinationに存在しない子要素は無視されます。\nルートオブジェクト自身のアクティブ状態は同期しません。", MessageType.Info);

      sourceObject = (GameObject)EditorGUILayout.ObjectField("Source", sourceObject, typeof(GameObject), true);
      destinationObject = (GameObject)EditorGUILayout.ObjectField("Destination", destinationObject, typeof(GameObject), true);

      if (GUILayout.Button("アクティブ状態を同期"))
      {
        // public staticなExecuteメソッドを呼び出す
        Execute(sourceObject, destinationObject);

        // 成功したことをユーザーに通知
        if (sourceObject != null && destinationObject != null)
        {
            // EditorUtility.DisplayDialog("成功", "アクティブ状態の同期が完了しました。", "OK");
        }
      }
    }

    /// <summary>
    /// 再帰的にオブジェクトのアクティブ状態を同期する
    /// </summary>
    private static void RecursiveSync(Transform source, Transform destination)
    {
      if (destination.gameObject.activeSelf != source.gameObject.activeSelf)
      {
        Undo.RecordObject(destination.gameObject, "Sync Active State");
        destination.gameObject.SetActive(source.gameObject.activeSelf);
      }

      foreach (Transform sourceChild in source)
      {
        Transform destinationChild = destination.Find(sourceChild.name);
        if (destinationChild != null)
        {
          RecursiveSync(sourceChild, destinationChild);
        }
      }
    }
  }
}
#endif