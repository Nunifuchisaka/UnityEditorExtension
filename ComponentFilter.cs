#if UNITY_EDITOR
using UnityEngine;

namespace Nunifuchisaka
{
  /// <summary>
  /// VRChat SDK / Modular Avatar / AAO / FloorAdjuster のコンポーネント判定を一元化する。
  /// これらのパッケージへのアセンブリ参照を持たないため、型名・名前空間の文字列マッチで判定する。
  /// </summary>
  public static class ComponentFilter
  {
    public static bool IsVrc(Component component)
    {
      var type = component.GetType();
      return type.Name.StartsWith("VRC") || (type.Namespace ?? "").StartsWith("VRC");
    }

    public static bool IsModularAvatar(Component component)
    {
      return component.GetType().Name.StartsWith("ModularAvatar");
    }

    public static bool IsAao(Component component)
    {
      return component.GetType().Name == "TraceAndOptimize";
    }

    public static bool IsFloorAdjuster(Component component)
    {
      return component.GetType().Name == "FloorAdjuster";
    }

    /// <summary>
    /// VRChat SDK の PipelineManager（アップロード先の Blueprint ID を保持する）か。
    /// </summary>
    public static bool IsPipelineManager(Component component)
    {
      return component != null && component.GetType().Name == "PipelineManager";
    }

    /// <summary>
    /// 有効化された条件のいずれかにコンポーネントが該当するかを返す。
    /// </summary>
    public static bool Matches(Component component, bool vrc, bool modularAvatar, bool aao, bool floorAdjuster)
    {
      return (vrc && IsVrc(component)) ||
             (modularAvatar && IsModularAvatar(component)) ||
             (aao && IsAao(component)) ||
             (floorAdjuster && IsFloorAdjuster(component));
    }

    /// <summary>
    /// どのモードでもコンポーネントコピーの対象にしないコンポーネント。
    /// Transformは TransformCopier、Renderer（Material・BlendShape）は MaterialCopier / BlendShapeCopier が担当する。
    /// MeshFilterはRendererとペアで意味を持つため、Rendererと合わせて除外する。
    /// （ただし ComponentCopier の All Components モードで新規に複製するオブジェクトは、これらも含めて丸ごと複製される）
    /// </summary>
    public static bool IsAlwaysExcluded(Component component)
    {
      return component is Transform || component is Renderer || component is MeshFilter;
    }

    /// <summary>
    /// ComponentCopier / AvatarComparer のコピー（比較）対象かを返す。
    /// all が true のときは IsAlwaysExcluded 以外のすべてが対象になる。
    /// </summary>
    public static bool IsCopyTarget(Component component, bool vrc, bool modularAvatar, bool aao, bool floorAdjuster, bool all)
    {
      if (component == null || IsAlwaysExcluded(component)) return false;
      return all || Matches(component, vrc, modularAvatar, aao, floorAdjuster);
    }
  }
}
#endif
