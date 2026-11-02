#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.Rendering;

namespace Nunifuchisaka
{
  public class ComponentCopier : EditorWindow
  {
    private GameObject sourceObject;
    private GameObject destinationObject;

    private bool copyAllComponents = true;
    private bool copyVrcComponents = true;
    private bool copyMaComponents = true;
    private bool copyAaoComponents = true;
    private bool copyFloorAdjuster = true;
    private bool keepDestBlueprintId = true;

    public const string KeepBlueprintIdTooltip = "コピー先のPipelineManagerに既にBlueprint IDがある場合は上書きせずに残します（別アバターとしてアップロード済みのアバターを誤って上書きしないため）。コピー先が空の場合は移行元のIDを引き継ぎます。";

    [MenuItem("Tools/Nunifuchisaka/Component Copier...", false, 100)]
    public static void ShowWindow()
    {
      GetWindow<ComponentCopier>("Component Copier");
    }

    private void OnGUI()
    {
      GUILayout.Label("Copy Components", EditorStyles.boldLabel);
      DocumentationLink.Draw();
      sourceObject = (GameObject)EditorGUILayout.ObjectField("From", sourceObject, typeof(GameObject), true);
      destinationObject = (GameObject)EditorGUILayout.ObjectField("To", destinationObject, typeof(GameObject), true);
      GUILayout.Space(10);
      EditorGUILayout.LabelField("Conditions", EditorStyles.boldLabel);
      copyAllComponents = EditorGUILayout.Toggle(new GUIContent("All Components", "Transform・Renderer・MeshFilterを除くすべてのコンポーネントをコピーします（既存のAnimatorは上書きしません）。コピー先に存在しないオブジェクトは子孫ごと丸ごと複製します。"), copyAllComponents);
      EditorGUI.BeginDisabledGroup(copyAllComponents);
      copyVrcComponents = EditorGUILayout.Toggle(new GUIContent("VRC", "VRChat SDK関連のコンポーネントをコピーします。"), copyVrcComponents);
      copyMaComponents = EditorGUILayout.Toggle(new GUIContent("ModularAvatar", "Modular Avatar関連のコンポーネントをコピーします。"), copyMaComponents);
      copyAaoComponents = EditorGUILayout.Toggle(new GUIContent("AAO TraceAndOptimize", "TraceAndOptimizeコンポーネントをコピーします。"), copyAaoComponents);
      copyFloorAdjuster = EditorGUILayout.Toggle(new GUIContent("FloorAdjuster", "FloorAdjusterコンポーネントをコピーします。"), copyFloorAdjuster);
      EditorGUI.EndDisabledGroup();
      keepDestBlueprintId = EditorGUILayout.Toggle(new GUIContent("Keep Dest Blueprint ID", KeepBlueprintIdTooltip), keepDestBlueprintId);

      GUILayout.Space(20);
      if (GUILayout.Button("Copy Components"))
      {
        ExecuteCopy(sourceObject, destinationObject, copyVrcComponents, copyMaComponents, copyAaoComponents, copyFloorAdjuster, copyAllComponents, keepDestBlueprintId);
      }
    }

    public static void ExecuteCopy(GameObject source, GameObject dest, bool doCopyVrc, bool doCopyMa, bool doCopyAao, bool doCopyFloorAdjuster, bool doCopyAll = false, bool keepDestBlueprintId = true)
    {
      if (!AvatarCopier.ValidateSourceAndDestination(source, dest, "[ComponentCopier]")) return;
      if (!doCopyAll && !doCopyVrc && !doCopyMa && !doCopyAao && !doCopyFloorAdjuster)
      {
        Debug.LogWarning("[ComponentCopier] コピーする条件を少なくとも1つチェックしてください。");
        return;
      }

      var context = new CopyContext
      {
        CopyVrc = doCopyVrc,
        CopyMa = doCopyMa,
        CopyAao = doCopyAao,
        CopyFloorAdjuster = doCopyFloorAdjuster,
        CopyAll = doCopyAll,
        KeepDestBlueprintId = keepDestBlueprintId,
      };

      Undo.SetCurrentGroupName("Copy Components Recursively");
      int group = Undo.GetCurrentGroup();
      try
      {
        Debug.Log($"[ComponentCopier] ----- Start copying from '{source.name}' to '{dest.name}' -----" + (doCopyAll ? " (All Components)" : ""));
        Debug.Log("[ComponentCopier] Pass 1: Replicating hierarchy...");
        ReplicateHierarchyRecursively(source.transform, dest.transform, context);
        Debug.Log("[ComponentCopier] Pass 2: Copying components...");
        CopyComponentsRecursively(source.transform, dest.transform, context);
        Debug.Log("[ComponentCopier] Pass 3: Remapping references...");
        RemapAllReferencesRecursively(source, dest);
        Debug.Log("[ComponentCopier] ----- All passes completed successfully. -----");
      }
      catch (System.Exception e)
      {
        Debug.LogError($"[ComponentCopier] An error occurred: {e.Message}\n{e.StackTrace}");
      }
      finally
      {
        Undo.CollapseUndoOperations(group);
      }
    }

    private class CopyContext
    {
      public bool CopyVrc;
      public bool CopyMa;
      public bool CopyAao;
      public bool CopyFloorAdjuster;
      public bool CopyAll;
      public bool KeepDestBlueprintId;
      // Pass 1 で子孫ごと丸ごと複製したオブジェクト（Pass 2 ではコピー済みのためスキップする）
      public readonly HashSet<Transform> ClonedRoots = new HashSet<Transform>();

      public bool IsTarget(Component component)
      {
        return ComponentFilter.IsCopyTarget(component, CopyVrc, CopyMa, CopyAao, CopyFloorAdjuster, CopyAll);
      }
    }

    private static void ReplicateHierarchyRecursively(Transform source, Transform parentInDest, CopyContext context)
    {
      foreach (Transform sourceChild in source)
      {
        if (!HierarchyHasCopyableComponents(sourceChild, context)) continue;

        Transform destChild = parentInDest.Find(sourceChild.name);
        if (destChild == null && context.CopyAll)
        {
          // All Components モード：コピー先に存在しないオブジェクトは子孫ごと丸ごと複製する。
          // コンポーネントだけを移すと Renderer / MeshFilter（常に除外）が欠けて見た目のないオブジェクトになるため。
          // Instantiate は位置・回転・スケール・アクティブ状態も引き継ぎ、複製範囲内の参照は複製側へ付け替える。
          // 複製範囲外（アバターの他の部分）への参照は Pass 3 でリマップする。
          GameObject clone = Object.Instantiate(sourceChild.gameObject, parentInDest);
          clone.name = sourceChild.name;
          Undo.RegisterCreatedObjectUndo(clone, "Duplicate GameObject");
          context.ClonedRoots.Add(clone.transform);
          Debug.Log($"[ComponentCopier] '{sourceChild.name}' はコピー先に存在しないため、子孫ごと複製しました。", clone);
          continue;
        }
        if (destChild == null)
        {
          GameObject newDestGo = new GameObject(sourceChild.name);
          Undo.RegisterCreatedObjectUndo(newDestGo, "Create GameObject");
          destChild = newDestGo.transform;
          destChild.SetParent(parentInDest);
          destChild.localPosition = sourceChild.localPosition;
          destChild.localRotation = sourceChild.localRotation;
          destChild.localScale = sourceChild.localScale;
          // アクティブ状態もコピーする（SyncActiveStateはComponentCopierより先に実行されるため、ここで作られたオブジェクトは対象外）
          newDestGo.SetActive(sourceChild.gameObject.activeSelf);
        }
        ReplicateHierarchyRecursively(sourceChild, destChild, context);
      }
    }

    private static bool HierarchyHasCopyableComponents(Transform target, CopyContext context)
    {
      foreach (var component in target.GetComponents<Component>())
      {
        if (context.IsTarget(component)) return true;
      }

      foreach (Transform child in target)
      {
        if (HierarchyHasCopyableComponents(child, context)) return true;
      }
      return false;
    }

    private static void CopyComponentsRecursively(Transform source, Transform destination, CopyContext context)
    {
      Component[] sourceComponents = source.GetComponents<Component>().OrderBy(c => GetSortPriority(c)).ToArray();
      // 同じ型のコンポーネントが同一GameObject上に複数ある場合、型ごとの出現順（n番目同士）で対応付ける
      var typeOccurrence = new Dictionary<System.Type, int>();
      foreach (var sourceComponent in sourceComponents)
      {
        if (!context.IsTarget(sourceComponent)) continue;

        System.Type componentType = sourceComponent.GetType();
        int occurrenceIndex;
        typeOccurrence.TryGetValue(componentType, out occurrenceIndex);
        typeOccurrence[componentType] = occurrenceIndex + 1;

        Component[] destComponents = destination.GetComponents(componentType);
        Component destComponent;
        if (occurrenceIndex < destComponents.Length)
        {
          destComponent = destComponents[occurrenceIndex];
          if (destComponent is Animator)
          {
            // 既存のAnimatorはコピー先のFBX由来（Humanoid Avatarアセット等）のため上書きしない。
            // 素体を差し替えた場合に、移行元のAvatarアセットでボーンの対応が壊れるのを防ぐ。
            Debug.Log($"[ComponentCopier] '{destination.name}' の既存のAnimatorは上書きせずに残しました。", destComponent);
            continue;
          }
          Undo.RecordObject(destComponent, "Modify Component");
        }
        else
        {
          destComponent = Undo.AddComponent(destination.gameObject, componentType);
        }

        // コピー先のBlueprint IDは貼り付けで上書きされるため、事前に控えておく
        string destBlueprintId = context.KeepDestBlueprintId ? GetBlueprintId(destComponent) : null;

        if (UnityEditorInternal.ComponentUtility.CopyComponent(sourceComponent))
        {
          UnityEditorInternal.ComponentUtility.PasteComponentValues(destComponent);
        }

        if (!string.IsNullOrEmpty(destBlueprintId) && GetBlueprintId(destComponent) != destBlueprintId)
        {
          SetBlueprintId(destComponent, destBlueprintId);
          Debug.Log($"[ComponentCopier] '{destination.name}' のBlueprint ID（{destBlueprintId}）は上書きせずに残しました。", destComponent);
        }
      }
      foreach (Transform sourceChild in source)
      {
        Transform destinationChild = destination.Find(sourceChild.name);
        if (destinationChild == null) continue;
        if (context.ClonedRoots.Contains(destinationChild)) continue;
        CopyComponentsRecursively(sourceChild, destinationChild, context);
      }
    }

    // PipelineManager.blueprintId を読み書きする（VRChat SDKへのアセンブリ参照を持たないため SerializedObject 経由）
    private const string BlueprintIdProperty = "blueprintId";

    private static string GetBlueprintId(Component component)
    {
      if (!ComponentFilter.IsPipelineManager(component)) return null;
      var property = new SerializedObject(component).FindProperty(BlueprintIdProperty);
      return property != null && property.propertyType == SerializedPropertyType.String ? property.stringValue : null;
    }

    private static void SetBlueprintId(Component component, string blueprintId)
    {
      var serializedObject = new SerializedObject(component);
      var property = serializedObject.FindProperty(BlueprintIdProperty);
      if (property == null || property.propertyType != SerializedPropertyType.String) return;
      property.stringValue = blueprintId;
      serializedObject.ApplyModifiedProperties();
    }

    private static void RemapAllReferencesRecursively(GameObject sourceRoot, GameObject destRoot)
    {
      RemapAllReferencesRecursivelyInternal(sourceRoot, destRoot, destRoot.transform);
    }

    private static void RemapAllReferencesRecursivelyInternal(GameObject sourceRoot, GameObject destRoot, Transform currentDest)
    {
      foreach (Component component in currentDest.GetComponents<Component>())
      {
        if (component == null) continue;
        if (component is Transform) continue;

        if (component is MonoBehaviour)
        {
          // マネージドコンポーネント（VRC / MA / 自作ギミックなど）はリフレクションでフィールドを辿る
          Undo.RecordObject(component, "Remap References");
          ProcessFieldsRecursively(component, component.GetType(), sourceRoot, destRoot, MaxRemapDepth);
          // リフレクションでのフィールド書き込みはUnityに変更が通知されないため、明示的に永続化する
          PrefabUtility.RecordPrefabInstancePropertyModifications(component);
          EditorUtility.SetDirty(component);
        }
        else
        {
          // ネイティブ実装の標準コンポーネント（Constraint・Joint・Cloth・SkinnedMeshRendererのbonesなど）は
          // C#フィールドがリフレクションから見えないため、SerializedObject 経由でリマップする
          RemapSerializedReferences(component, sourceRoot, destRoot);
        }
      }
      foreach (Transform child in currentDest)
      {
        RemapAllReferencesRecursivelyInternal(sourceRoot, destRoot, child);
      }
    }

    private static void RemapSerializedReferences(Component component, GameObject sourceRoot, GameObject destRoot)
    {
      var serializedObject = new SerializedObject(component);
      SerializedProperty property = serializedObject.GetIterator();
      bool changed = false;
      while (property.Next(true))
      {
        if (property.propertyType != SerializedPropertyType.ObjectReference) continue;
        if (IsInternalSerializedProperty(property)) continue;

        UnityEngine.Object current = property.objectReferenceValue;
        if (current == null) continue;
        UnityEngine.Object remapped = RemapObject(current, sourceRoot, destRoot);
        if (remapped != null && remapped != current)
        {
          property.objectReferenceValue = remapped;
          changed = true;
        }
      }
      // ApplyModifiedProperties は Undo 記録と Prefab オーバーライドの記録も行う
      if (changed) serializedObject.ApplyModifiedProperties();
    }

    // コンポーネント自身の所属やPrefab接続を表す内部プロパティ。リマップ対象にしない
    private static bool IsInternalSerializedProperty(SerializedProperty property)
    {
      if (property.depth != 0) return false;
      switch (property.name)
      {
        case "m_GameObject":
        case "m_CorrespondingSourceObject":
        case "m_PrefabInstance":
        case "m_PrefabAsset":
          return true;
        default:
          return false;
      }
    }

    private static int GetSortPriority(Component component)
    {
      if (component == null) return 99;

      string componentName = component.GetType().Name;
      if (componentName == "VRCPhysBoneCollider") return 0;
      if (componentName == "VRCPhysBone") return 1;
      if (componentName.StartsWith("ModularAvatar")) return 3;
      if (componentName == "TraceAndOptimize") return 4;
      if (componentName == "FloorAdjuster") return 5;
      return 2;
    }
    
    // ネストしたSerializableクラス・構造体を再帰する深さの上限（自己参照による無限再帰を防ぐ）
    private const int MaxRemapDepth = 6;

    private static void ProcessFieldsRecursively(object targetObject, System.Type targetType, GameObject sourceRoot, GameObject destRoot, int depth)
    {
      if (targetObject == null || depth <= 0) return;
      FieldInfo[] fields = targetType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
      foreach (FieldInfo field in fields)
      {
        object fieldValue = field.GetValue(targetObject);
        ProcessValue(fieldValue, field.FieldType, (newValue) => field.SetValue(targetObject, newValue), sourceRoot, destRoot, depth);
      }
    }

    private static void ProcessValue(object value, System.Type valueType, System.Action<object> setter, GameObject sourceRoot, GameObject destRoot, int depth)
    {
      if (value == null || depth <= 0) return;
      if (typeof(UnityEngine.Object).IsAssignableFrom(valueType))
      {
        var newReference = RemapObject((UnityEngine.Object)value, sourceRoot, destRoot);
        if (newReference != null) setter(newReference);
      }
      else if (valueType.IsArray)
      {
        var array = value as System.Array;
        System.Type elementType = valueType.GetElementType();
        if (typeof(UnityEngine.Object).IsAssignableFrom(elementType))
        {
          for (int i = 0; i < array.Length; i++)
            if (array.GetValue(i) is UnityEngine.Object elem)
            {
              var newReference = RemapObject(elem, sourceRoot, destRoot);
              if (newReference != null) array.SetValue(newReference, i);
            }
        }
        else if (IsRecursableType(elementType))
        {
          // 構造体・Serializableクラス要素の中の参照もリマップする（例：VRCHeadChop.targetBones[n].transform）
          for (int i = 0; i < array.Length; i++)
          {
            object element = array.GetValue(i);
            if (element == null) continue;
            ProcessFieldsRecursively(element, element.GetType(), sourceRoot, destRoot, depth - 1);
            if (elementType.IsValueType) array.SetValue(element, i);
          }
        }
      }
      else if (typeof(IList).IsAssignableFrom(valueType) && valueType.IsGenericType)
      {
        var list = value as IList;
        System.Type genericType = valueType.GetGenericArguments()[0];
        if (typeof(UnityEngine.Object).IsAssignableFrom(genericType))
        {
          for (int i = 0; i < list.Count; i++)
            if(list[i] is UnityEngine.Object elem)
            {
              var newReference = RemapObject(elem, sourceRoot, destRoot);
              if (newReference != null) list[i] = newReference;
            }
        }
        else if (IsRecursableType(genericType))
        {
          for (int i = 0; i < list.Count; i++)
          {
            object element = list[i];
            if (element == null) continue;
            ProcessFieldsRecursively(element, element.GetType(), sourceRoot, destRoot, depth - 1);
            if (genericType.IsValueType) list[i] = element;
          }
        }
      }
      else if (IsRecursableType(valueType))
      {
        // 構造体に加え、Serializableクラスの中も再帰する（例：ModularAvatarのAvatarObjectReference.targetObject）
        ProcessFieldsRecursively(value, value.GetType(), sourceRoot, destRoot, depth - 1);
        if (valueType.IsValueType) setter(value);
      }
    }

    private static bool IsRecursableType(System.Type type)
    {
      if (type.IsPrimitive || type.IsEnum || type == typeof(string)) return false;
      if (typeof(UnityEngine.Object).IsAssignableFrom(type)) return false;
      string ns = type.Namespace ?? "";
      if (ns.StartsWith("System")) return false;
      if (type.IsClass && ns.StartsWith("UnityEngine")) return false;
      return true;
    }

    private static UnityEngine.Object RemapObject(UnityEngine.Object sourceRefObject, GameObject sourceRoot, GameObject destRoot)
    {
      if (sourceRefObject == null) return null;
      GameObject sourceGo = null;
      if (sourceRefObject is GameObject) sourceGo = (GameObject)sourceRefObject;
      if (sourceRefObject is Component) sourceGo = ((Component)sourceRefObject).gameObject;
      if (sourceGo == null || !sourceGo.transform.IsChildOf(sourceRoot.transform)) return null;
      string path = GetRelativePath(sourceRoot.transform, sourceGo.transform);
      Transform destinationTransform = (path == null) ? destRoot.transform : destRoot.transform.Find(path);
      if (destinationTransform == null) return null;
      if (sourceRefObject is GameObject) return destinationTransform.gameObject;
      if (sourceRefObject is Component sourceComponent)
      {
        // 同じ型のコンポーネントが複数ある場合は、Pass 2 と同じく出現順のn番目同士で対応付ける
        System.Type type = sourceComponent.GetType();
        int index = System.Array.IndexOf(sourceGo.GetComponents(type), sourceComponent);
        Component[] destComponents = destinationTransform.GetComponents(type);
        if (index < 0 || index >= destComponents.Length) return null;
        return destComponents[index];
      }
      return null;
    }
    private static string GetRelativePath(Transform root, Transform child)
    {
      if (child == root) return null;
      string path = child.name;
      Transform parent = child.parent;
      while (parent != null)
      {
        if (parent == root) return path;
        path = parent.name + "/" + path;
        parent = parent.parent;
      }
      return null;
    }
  }
}
#endif