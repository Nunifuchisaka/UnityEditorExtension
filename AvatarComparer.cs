#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Nunifuchisaka
{
  /// <summary>
  /// コピー元とコピー先のアバターを比較し、移行漏れや差異を検出するエディタ拡張。
  /// AvatarCopierの各ステップ（階層・Transform・Material・BlendShape・Active・コンポーネント）に対応する。
  /// </summary>
  public class AvatarComparer : EditorWindow
  {
    public enum Severity { Error, Warning }

    public class Difference
    {
      public Severity Severity;
      public string Category;
      public string Path;
      public string Message;
      public UnityEngine.Object Target;
    }

    public class ComparisonReport
    {
      public readonly List<Difference> Differences = new List<Difference>();
      public int ObjectCount;
      public int ComponentCount;
      public bool Truncated;
      private const int MaxDifferences = 300;

      public int ErrorCount { get { return Differences.Count(d => d.Severity == Severity.Error); } }
      public int WarningCount { get { return Differences.Count(d => d.Severity == Severity.Warning); } }

      public void Add(Severity severity, string category, string path, string message, UnityEngine.Object target)
      {
        if (Differences.Count >= MaxDifferences)
        {
          Truncated = true;
          return;
        }
        Differences.Add(new Difference { Severity = severity, Category = category, Path = path, Message = message, Target = target });
      }
    }

    private GameObject sourceObject;
    private GameObject destinationObject;
    private bool compareAllComponents = true;
    private ComparisonReport report;
    private Vector2 scrollPosition;

    [MenuItem("Tools/Nunifuchisaka/Avatar Comparer...", false, 2)]
    public static void ShowWindow()
    {
      GetWindow<AvatarComparer>("Avatar Comparer");
    }

    /// <summary>
    /// From/Toを指定してウィンドウを開く（AvatarCopierからの連携用）。
    /// </summary>
    public static void ShowWindow(GameObject source, GameObject dest, bool allComponents = true)
    {
      var window = GetWindow<AvatarComparer>("Avatar Comparer");
      window.sourceObject = source;
      window.destinationObject = dest;
      window.compareAllComponents = allComponents;
    }

    private void OnGUI()
    {
      GUILayout.Label("アバター移行の比較確認", EditorStyles.boldLabel);
      DocumentationLink.Draw();
      EditorGUILayout.HelpBox("Fromを基準に、階層・Transform・Material・BlendShape・アクティブ状態・コンポーネントと参照を比較し、移行漏れや差異を検出します。同名の兄弟オブジェクトも警告します。", MessageType.Info);

      sourceObject = (GameObject)EditorGUILayout.ObjectField("From", sourceObject, typeof(GameObject), true);
      destinationObject = (GameObject)EditorGUILayout.ObjectField("To", destinationObject, typeof(GameObject), true);
      compareAllComponents = EditorGUILayout.Toggle(new GUIContent("All Components", "Transform・Renderer・MeshFilterを除くすべてのコンポーネントを比較します（Component Copier の All Components モードに対応）。OFFのときは VRC / Modular Avatar / AAO / FloorAdjuster のみを比較します。"), compareAllComponents);

      GUILayout.Space(10);

      EditorGUI.BeginDisabledGroup(sourceObject == null || destinationObject == null);
      if (GUILayout.Button("Compare", GUILayout.Height(30)))
      {
        report = Execute(sourceObject, destinationObject, compareAllComponents);
      }
      EditorGUI.EndDisabledGroup();

      if (report == null) return;

      GUILayout.Space(10);
      GUILayout.Label($"結果: エラー {report.ErrorCount} 件 / 警告 {report.WarningCount} 件（オブジェクト {report.ObjectCount} 件・コンポーネント {report.ComponentCount} 件を比較）", EditorStyles.boldLabel);

      if (report.Differences.Count == 0)
      {
        EditorGUILayout.HelpBox("差異は見つかりませんでした。", MessageType.Info);
        return;
      }
      if (report.Truncated)
      {
        EditorGUILayout.HelpBox("差異が多いため、表示を打ち切りました。大きな差異から解消して再実行してください。", MessageType.Warning);
      }

      scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, "box");
      foreach (var diff in report.Differences)
      {
        EditorGUILayout.BeginHorizontal();

        Color originalColor = GUI.color;
        GUI.color = diff.Severity == Severity.Error ? new Color(1f, 0.55f, 0.55f) : new Color(1f, 0.9f, 0.5f);
        GUILayout.Label(diff.Severity == Severity.Error ? "ERROR" : "WARN", EditorStyles.miniBoldLabel, GUILayout.Width(48));
        GUI.color = originalColor;

        EditorGUILayout.BeginVertical();
        EditorGUILayout.LabelField($"[{diff.Category}] {(string.IsNullOrEmpty(diff.Path) ? "(root)" : diff.Path)}", EditorStyles.miniBoldLabel);
        EditorGUILayout.LabelField(diff.Message, EditorStyles.wordWrappedMiniLabel);
        EditorGUILayout.EndVertical();

        EditorGUI.BeginDisabledGroup(diff.Target == null);
        if (GUILayout.Button("Select", GUILayout.Width(50)))
        {
          Selection.activeObject = diff.Target;
          EditorGUIUtility.PingObject(diff.Target);
        }
        EditorGUI.EndDisabledGroup();

        EditorGUILayout.EndHorizontal();
      }
      EditorGUILayout.EndScrollView();
    }

    public static ComparisonReport Execute(GameObject source, GameObject dest, bool allComponents = true)
    {
      if (!AvatarCopier.ValidateSourceAndDestination(source, dest, "[AvatarComparer]")) return null;

      var report = new ComparisonReport();

      // 同名の兄弟オブジェクトは全ツールで正しく対応付けできないため、先に警告する
      foreach (var duplicate in FindDuplicateSiblings(source))
      {
        report.Add(Severity.Warning, "Hierarchy", duplicate.Key, "From側に同名の兄弟オブジェクトがあり、正しく対応付けできません（1つ目だけが比較・コピーの対象になります）", duplicate.Value.gameObject);
      }
      foreach (var duplicate in FindDuplicateSiblings(dest))
      {
        report.Add(Severity.Warning, "Hierarchy", duplicate.Key, "To側に同名の兄弟オブジェクトがあり、正しく対応付けできません（1つ目だけが比較・コピーの対象になります）", duplicate.Value.gameObject);
      }
      CompareRecursively(source.transform, dest.transform, source, dest, "", allComponents, report);

      // ウィンドウを閉じても確認できるよう、明細もConsoleに出力する
      foreach (var diff in report.Differences)
      {
        string line = $"[AvatarComparer] {(diff.Severity == Severity.Error ? "ERROR" : "WARN")} [{diff.Category}] {(string.IsNullOrEmpty(diff.Path) ? "(root)" : diff.Path)} — {diff.Message}";
        if (diff.Severity == Severity.Error) Debug.LogError(line, diff.Target);
        else Debug.LogWarning(line, diff.Target);
      }

      string summary = $"[AvatarComparer] 比較完了: エラー {report.ErrorCount} 件 / 警告 {report.WarningCount} 件（オブジェクト {report.ObjectCount} 件・コンポーネント {report.ComponentCount} 件を比較）";
      if (report.ErrorCount > 0) Debug.LogWarning(summary);
      else Debug.Log(summary);
      return report;
    }

    private static void CompareRecursively(Transform src, Transform dst, GameObject sourceRoot, GameObject destRoot, string path, bool allComponents, ComparisonReport report)
    {
      report.ObjectCount++;
      bool isRoot = path.Length == 0;

      // ルート同士はシーン内の配置・表示状態が異なって当然なので、アクティブ状態とTransformは比較しない
      if (!isRoot)
      {
        // アクティブ状態（SyncActiveState相当）
        if (src.gameObject.activeSelf != dst.gameObject.activeSelf)
        {
          report.Add(Severity.Warning, "Active", path, $"アクティブ状態が不一致（From: {src.gameObject.activeSelf} → To: {dst.gameObject.activeSelf}）", dst.gameObject);
        }

        // Transform
        if (!Approx(src.localPosition, dst.localPosition))
          report.Add(Severity.Warning, "Transform", path, $"localPosition が不一致（From: {Format(src.localPosition)} → To: {Format(dst.localPosition)}）", dst.gameObject);
        if (Quaternion.Angle(src.localRotation, dst.localRotation) > 0.01f)
          report.Add(Severity.Warning, "Transform", path, $"localRotation が不一致（From: {Format(src.localEulerAngles)} → To: {Format(dst.localEulerAngles)}）", dst.gameObject);
        if (!Approx(src.localScale, dst.localScale))
          report.Add(Severity.Warning, "Transform", path, $"localScale が不一致（From: {Format(src.localScale)} → To: {Format(dst.localScale)}）", dst.gameObject);
      }

      CompareMaterials(src, dst, path, report);
      CompareBlendShapes(src, dst, path, report);
      CompareComponents(src, dst, sourceRoot, destRoot, path, allComponents, report);

      foreach (Transform srcChild in src)
      {
        string childPath = isRoot ? srcChild.name : path + "/" + srcChild.name;
        Transform dstChild = dst.Find(srcChild.name);
        if (dstChild == null)
        {
          if (ContainsMigrationTarget(srcChild, allComponents))
          {
            report.Add(Severity.Error, "Hierarchy", childPath, "コピー先に存在しません（コピー対象のPrefabインスタンスまたはコンポーネントを含む）", srcChild.gameObject);
          }
          continue;
        }
        CompareRecursively(srcChild, dstChild, sourceRoot, destRoot, childPath, allComponents, report);
      }
    }

    private static bool ContainsMigrationTarget(Transform target, bool allComponents)
    {
      if (PrefabUtility.IsAnyPrefabInstanceRoot(target.gameObject)) return true;
      foreach (var component in target.GetComponents<Component>())
      {
        if (ComponentFilter.IsCopyTarget(component, true, true, true, true, allComponents)) return true;
      }
      foreach (Transform child in target)
      {
        if (ContainsMigrationTarget(child, allComponents)) return true;
      }
      return false;
    }

    private static void CompareMaterials(Transform src, Transform dst, string path, ComparisonReport report)
    {
      var sourceRenderer = src.GetComponent<Renderer>();
      var destRenderer = dst.GetComponent<Renderer>();
      if (sourceRenderer == null) return;
      if (destRenderer == null)
      {
        report.Add(Severity.Warning, "Material", path, "コピー先にRendererがありません", dst.gameObject);
        return;
      }

      var sourceMaterials = sourceRenderer.sharedMaterials;
      var destMaterials = destRenderer.sharedMaterials;
      if (sourceMaterials.Length != destMaterials.Length)
      {
        report.Add(Severity.Warning, "Material", path, $"マテリアル数が不一致（From: {sourceMaterials.Length} → To: {destMaterials.Length}）", dst.gameObject);
        return;
      }
      for (int i = 0; i < sourceMaterials.Length; i++)
      {
        if (sourceMaterials[i] != destMaterials[i])
        {
          report.Add(Severity.Warning, "Material", path, $"マテリアル[{i}] が不一致（From: '{ObjectName(sourceMaterials[i])}' → To: '{ObjectName(destMaterials[i])}'）", dst.gameObject);
        }
      }
    }

    private static void CompareBlendShapes(Transform src, Transform dst, string path, ComparisonReport report)
    {
      var sourceSmr = src.GetComponent<SkinnedMeshRenderer>();
      var destSmr = dst.GetComponent<SkinnedMeshRenderer>();
      if (sourceSmr == null || destSmr == null) return;
      if (sourceSmr.sharedMesh == null || destSmr.sharedMesh == null) return;

      var mismatches = new List<string>();
      for (int i = 0; i < sourceSmr.sharedMesh.blendShapeCount; i++)
      {
        string blendShapeName = sourceSmr.sharedMesh.GetBlendShapeName(i);
        int destIndex = destSmr.sharedMesh.GetBlendShapeIndex(blendShapeName);
        if (destIndex == -1) continue;

        float sourceWeight = sourceSmr.GetBlendShapeWeight(i);
        float destWeight = destSmr.GetBlendShapeWeight(destIndex);
        if (Mathf.Abs(sourceWeight - destWeight) > 0.01f)
        {
          mismatches.Add($"'{blendShapeName}' {sourceWeight:F1} → {destWeight:F1}");
        }
      }
      if (mismatches.Count > 0)
      {
        report.Add(Severity.Warning, "BlendShape", path, $"ウェイトが {mismatches.Count} 件不一致（{string.Join(", ", mismatches.Take(3))}{(mismatches.Count > 3 ? " ..." : "")}）", dst.gameObject);
      }
    }

    private static void CompareComponents(Transform src, Transform dst, GameObject sourceRoot, GameObject destRoot, string path, bool allComponents, ComparisonReport report)
    {
      var sourceComponents = src.GetComponents<Component>()
        .Where(c => ComponentFilter.IsCopyTarget(c, true, true, true, true, allComponents));

      foreach (var group in sourceComponents.GroupBy(c => c.GetType()))
      {
        var sourceList = group.ToArray();
        var destList = dst.GetComponents(group.Key);

        if (destList.Length < sourceList.Length)
        {
          report.Add(Severity.Error, "Component", path, $"'{group.Key.Name}' の数が不一致（From: {sourceList.Length} → To: {destList.Length}）", dst.gameObject);
          continue;
        }
        if (destList.Length > sourceList.Length)
        {
          report.Add(Severity.Warning, "Component", path, $"'{group.Key.Name}' がコピー先に多くあります（From: {sourceList.Length} → To: {destList.Length}）。コピー先独自のコンポーネントの可能性があります", dst.gameObject);
          continue;
        }

        for (int i = 0; i < sourceList.Length; i++)
        {
          report.ComponentCount++;
          string label = sourceList.Length > 1 ? $"{group.Key.Name}[{i}]" : group.Key.Name;

          if (sourceList[i] is Behaviour sourceBehaviour && destList[i] is Behaviour destBehaviour && sourceBehaviour.enabled != destBehaviour.enabled)
          {
            report.Add(Severity.Warning, "Component", path, $"{label}: enabled が不一致（From: {sourceBehaviour.enabled} → To: {destBehaviour.enabled}）", dst.gameObject);
          }

          // 既存のAnimatorはComponentCopierが意図的に上書きしない（コピー先FBX由来のAvatarアセットを保持する）ため、中身は比較しない
          if (sourceList[i] is Animator) continue;

          if (sourceList[i] is MonoBehaviour)
          {
            foreach (var field in GetSerializedFields(group.Key))
            {
              CompareValue(field.GetValue(sourceList[i]), field.GetValue(destList[i]), field.FieldType,
                           $"{label}.{field.Name}", path, sourceRoot, destRoot, dst.gameObject, report, 4);
            }
          }
          else
          {
            // ネイティブ実装の標準コンポーネントはC#フィールドが見えないため、SerializedObject経由で参照だけを比較する
            CompareSerializedReferences(sourceList[i], destList[i], label, path, sourceRoot, destRoot, dst.gameObject, report);
          }
        }
      }
    }

    private static void CompareSerializedReferences(Component src, Component dst, string label, string path,
                                                    GameObject sourceRoot, GameObject destRoot, UnityEngine.Object pingTarget,
                                                    ComparisonReport report)
    {
      var sourceSerialized = new SerializedObject(src);
      var destSerialized = new SerializedObject(dst);
      SerializedProperty property = sourceSerialized.GetIterator();
      while (property.Next(true))
      {
        if (property.propertyType != SerializedPropertyType.ObjectReference) continue;
        if (IsInternalSerializedProperty(property)) continue;

        SerializedProperty destProperty = destSerialized.FindProperty(property.propertyPath);
        if (destProperty == null)
        {
          // 配列の要素数が異なる場合など。コピー元側で参照が入っている場合のみ報告する
          if (property.objectReferenceValue != null)
          {
            report.Add(Severity.Warning, "Reference", path, $"{label}.{property.propertyPath}: コピー先に対応するプロパティがありません", pingTarget);
          }
          continue;
        }
        if (destProperty.propertyType != SerializedPropertyType.ObjectReference) continue;

        CompareObjectReference(property.objectReferenceValue, destProperty.objectReferenceValue,
                               $"{label}.{property.propertyPath}", path, sourceRoot, destRoot, pingTarget, report);
      }
    }

    // コンポーネント自身の所属やPrefab接続を表す内部プロパティ。比較対象にしない
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

    private static void CompareValue(object srcVal, object dstVal, Type declaredType, string label, string path,
                                     GameObject sourceRoot, GameObject destRoot, UnityEngine.Object pingTarget,
                                     ComparisonReport report, int depth)
    {
      // UnityEngine.Object参照：リマップの正しさを検証
      if (typeof(UnityEngine.Object).IsAssignableFrom(declaredType) || srcVal is UnityEngine.Object || dstVal is UnityEngine.Object)
      {
        CompareObjectReference(srcVal as UnityEngine.Object, dstVal as UnityEngine.Object, label, path, sourceRoot, destRoot, pingTarget, report);
        return;
      }

      if (srcVal == null && dstVal == null) return;
      if (srcVal == null || dstVal == null)
      {
        // シリアライズを経ると null が空のカーブ・空文字列・空リストに実体化されるため、両者は同一とみなす
        if (IsEffectivelyEmpty(srcVal) && IsEffectivelyEmpty(dstVal)) return;
        report.Add(Severity.Warning, "Component", path, $"{label}: 値が不一致（片方のみ null）", pingTarget);
        return;
      }

      if (srcVal is AnimationCurve sourceCurve && dstVal is AnimationCurve destCurve)
      {
        if (!CurvesEqual(sourceCurve, destCurve))
        {
          report.Add(Severity.Warning, "Component", path, $"{label}: カーブが不一致", pingTarget);
        }
        return;
      }

      if (srcVal is string sourceText)
      {
        if (sourceText != (string)dstVal)
        {
          report.Add(Severity.Warning, "Component", path, $"{label}: 値が不一致（'{srcVal}' → '{dstVal}'）", pingTarget);
        }
        return;
      }

      if (srcVal is float sourceFloat)
      {
        if (!Approx(sourceFloat, (float)dstVal))
        {
          report.Add(Severity.Warning, "Component", path, $"{label}: 値が不一致（{srcVal} → {dstVal}）", pingTarget);
        }
        return;
      }
      if (srcVal is double sourceDouble)
      {
        if (Math.Abs(sourceDouble - (double)dstVal) > 1e-6)
        {
          report.Add(Severity.Warning, "Component", path, $"{label}: 値が不一致（{srcVal} → {dstVal}）", pingTarget);
        }
        return;
      }

      if (srcVal is IList sourceList && dstVal is IList destList)
      {
        if (sourceList.Count != destList.Count)
        {
          report.Add(Severity.Warning, "Component", path, $"{label}: 要素数が不一致（From: {sourceList.Count} → To: {destList.Count}）", pingTarget);
          return;
        }
        Type elementType = declaredType.IsArray ? declaredType.GetElementType()
                         : declaredType.IsGenericType ? declaredType.GetGenericArguments()[0]
                         : typeof(object);
        for (int i = 0; i < sourceList.Count; i++)
        {
          CompareValue(sourceList[i], destList[i], elementType, $"{label}[{i}]", path, sourceRoot, destRoot, pingTarget, report, depth - 1);
        }
        return;
      }

      Type valueType = srcVal.GetType();
      if (valueType.IsPrimitive || valueType.IsEnum)
      {
        if (!srcVal.Equals(dstVal))
        {
          report.Add(Severity.Warning, "Component", path, $"{label}: 値が不一致（{srcVal} → {dstVal}）", pingTarget);
        }
        return;
      }

      // 構造体・Serializableクラスは中のフィールドを再帰比較（UnityEngine/System組み込みクラスは対象外）
      if (depth <= 0) return;
      if (valueType != dstVal.GetType()) return;
      string ns = valueType.Namespace ?? "";
      if (valueType.IsClass && (ns.StartsWith("UnityEngine") || ns.StartsWith("System"))) return;

      foreach (var field in GetSerializedFields(valueType))
      {
        CompareValue(field.GetValue(srcVal), field.GetValue(dstVal), field.FieldType,
                     $"{label}.{field.Name}", path, sourceRoot, destRoot, pingTarget, report, depth - 1);
      }
    }

    private static void CompareObjectReference(UnityEngine.Object srcObj, UnityEngine.Object dstObj, string label, string path,
                                               GameObject sourceRoot, GameObject destRoot, UnityEngine.Object pingTarget,
                                               ComparisonReport report)
    {
      bool srcNull = srcObj == null;
      bool dstNull = dstObj == null;
      if (srcNull && dstNull) return;

      Transform srcTransform = GetTransformOf(srcObj);
      bool srcInternal = !srcNull && srcTransform != null && srcTransform.IsChildOf(sourceRoot.transform);

      if (srcInternal)
      {
        // コピー元階層内への参照：コピー先の対応オブジェクトを指しているべき
        string sourcePath = GetRelativePath(sourceRoot.transform, srcTransform) ?? "(root)";
        if (dstNull)
        {
          report.Add(Severity.Error, "Reference", path, $"{label}: コピー元階層内 '{sourcePath}' への参照が、コピー先では null です（リマップ漏れ）", pingTarget);
          return;
        }
        Transform dstTransform = GetTransformOf(dstObj);
        if (dstTransform == null)
        {
          report.Add(Severity.Error, "Reference", path, $"{label}: 参照の種類が不一致（From: {srcObj.GetType().Name} → To: {dstObj.GetType().Name}）", pingTarget);
        }
        else if (dstTransform.IsChildOf(sourceRoot.transform))
        {
          report.Add(Severity.Error, "Reference", path, $"{label}: コピー元の '{sourcePath}' を参照したままです（リマップ漏れ）", pingTarget);
        }
        else if (!dstTransform.IsChildOf(destRoot.transform))
        {
          report.Add(Severity.Error, "Reference", path, $"{label}: コピー先階層の外を参照しています（'{ObjectName(dstObj)}'）", pingTarget);
        }
        else
        {
          string destPath = GetRelativePath(destRoot.transform, dstTransform) ?? "(root)";
          if (destPath != sourcePath)
          {
            report.Add(Severity.Error, "Reference", path, $"{label}: 参照先のパスが不一致（From: '{sourcePath}' → To: '{destPath}'）", pingTarget);
          }
          else if (srcObj is Component srcComponent && dstObj is Component dstComponent)
          {
            // 同じオブジェクト上に同型のコンポーネントが複数ある場合、何番目を指しているかまで一致しているべき
            int sourceIndex = GetComponentIndex(srcComponent);
            int destIndex = GetComponentIndex(dstComponent);
            if (srcComponent.GetType() != dstComponent.GetType())
            {
              report.Add(Severity.Error, "Reference", path, $"{label}: 参照先コンポーネントの型が不一致（From: {srcComponent.GetType().Name} → To: {dstComponent.GetType().Name}）", pingTarget);
            }
            else if (sourceIndex != destIndex)
            {
              report.Add(Severity.Error, "Reference", path, $"{label}: '{sourcePath}' の {srcComponent.GetType().Name} を参照していますが、何番目かが不一致です（From: {sourceIndex + 1}番目 → To: {destIndex + 1}番目）", pingTarget);
            }
          }
        }
        return;
      }

      // 外部参照（アセット等）：同一参照であるべき
      if (srcObj != dstObj)
      {
        Transform dstTransform = GetTransformOf(dstObj);
        if (dstTransform != null && dstTransform.IsChildOf(sourceRoot.transform))
        {
          report.Add(Severity.Error, "Reference", path, $"{label}: コピー元階層内の '{ObjectName(dstObj)}' を参照しています", pingTarget);
        }
        else
        {
          report.Add(Severity.Warning, "Reference", path, $"{label}: 参照が不一致（From: '{ObjectName(srcObj)}' → To: '{ObjectName(dstObj)}'）", pingTarget);
        }
      }
    }

    private static IEnumerable<FieldInfo> GetSerializedFields(Type type)
    {
      for (var t = type; t != null && t != typeof(UnityEngine.Object) && t != typeof(object); t = t.BaseType)
      {
        foreach (var field in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
          if (IsSerializedField(field)) yield return field;
        }
      }
    }

    private static bool IsSerializedField(FieldInfo field)
    {
      if (field.IsNotSerialized || field.IsInitOnly || field.IsLiteral) return false;
      if (field.IsPublic) return true;
      return field.GetCustomAttributes(typeof(SerializeField), true).Length > 0;
    }

    private static bool IsEffectivelyEmpty(object value)
    {
      if (value == null) return true;
      if (value is string text) return text.Length == 0;
      if (value is AnimationCurve curve) return curve.length == 0;
      if (value is IList list) return list.Count == 0;
      return false;
    }

    private static bool CurvesEqual(AnimationCurve a, AnimationCurve b)
    {
      if (a.length != b.length) return false;
      for (int i = 0; i < a.length; i++)
      {
        if (!Approx(a.keys[i].time, b.keys[i].time) || !Approx(a.keys[i].value, b.keys[i].value)) return false;
      }
      return true;
    }

    private static int GetComponentIndex(Component component)
    {
      return Array.IndexOf(component.gameObject.GetComponents(component.GetType()), component);
    }

    /// <summary>
    /// root 配下で、同じ階層に同名の兄弟がいるオブジェクトの相対パスを返す（名前ごとに1件）。
    /// </summary>
    public static List<string> FindDuplicateSiblingPaths(GameObject root)
    {
      return FindDuplicateSiblings(root).Select(pair => pair.Key).ToList();
    }

    private static List<KeyValuePair<string, Transform>> FindDuplicateSiblings(GameObject root)
    {
      var result = new List<KeyValuePair<string, Transform>>();
      if (root != null) CollectDuplicateSiblings(root.transform, "", result);
      return result;
    }

    private static void CollectDuplicateSiblings(Transform parent, string parentPath, List<KeyValuePair<string, Transform>> result)
    {
      var seen = new HashSet<string>();
      var reported = new HashSet<string>();
      foreach (Transform child in parent)
      {
        string childPath = parentPath.Length == 0 ? child.name : parentPath + "/" + child.name;
        if (!seen.Add(child.name) && reported.Add(child.name))
        {
          result.Add(new KeyValuePair<string, Transform>(childPath, child));
        }
        CollectDuplicateSiblings(child, childPath, result);
      }
    }

    private static Transform GetTransformOf(UnityEngine.Object obj)
    {
      if (obj is GameObject go) return go.transform;
      if (obj is Component component) return component.transform;
      return null;
    }

    private static string ObjectName(UnityEngine.Object obj)
    {
      return obj == null ? "null" : obj.name;
    }

    private static string Format(Vector3 v)
    {
      return $"({v.x:F4}, {v.y:F4}, {v.z:F4})";
    }

    private static bool Approx(float a, float b)
    {
      return Mathf.Abs(a - b) <= 1e-4f * Mathf.Max(1f, Mathf.Abs(a), Mathf.Abs(b));
    }

    private static bool Approx(Vector3 a, Vector3 b)
    {
      return Approx(a.x, b.x) && Approx(a.y, b.y) && Approx(a.z, b.z);
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
