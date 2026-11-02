#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System;

namespace Nunifuchisaka
{
  public static class MaterialCopier
  {
    public static void ExecuteCopy(GameObject source, GameObject dest)
    {
      if (!AvatarCopier.ValidateSourceAndDestination(source, dest, "[MaterialCopier]")) return;

      Undo.SetCurrentGroupName("Copy Materials");
      int group = Undo.GetCurrentGroup();

      try
      {
        int count = CopyMaterialsRecursively(source.transform, dest.transform);
        Debug.Log($"[MaterialCopier] Complete. {count} 個のRendererのマテリアルをコピーしました。");
      }
      catch (Exception e)
      {
        Debug.LogError($"[MaterialCopier] エラーが発生しました: {e.Message}\n{e.StackTrace}");
      }
      finally
      {
        Undo.CollapseUndoOperations(group);
      }
    }

    private static int CopyMaterialsRecursively(Transform source, Transform dest)
    {
      int count = 0;
      var sourceRenderer = source.GetComponent<Renderer>();
      var destRenderer = dest.GetComponent<Renderer>();

      // コピー元とコピー先の両方にRendererが存在する場合のみ、マテリアルをコピー
      if (sourceRenderer != null && destRenderer != null)
      {
        WarnIfSlotMismatch(sourceRenderer, destRenderer);
        Undo.RecordObject(destRenderer, "Copy Materials");
        destRenderer.sharedMaterials = sourceRenderer.sharedMaterials;
        count++;
      }

      foreach (Transform sourceChild in source)
      {
        Transform destChild = dest.Find(sourceChild.name);
        if (destChild != null)
        {
          count += CopyMaterialsRecursively(sourceChild, destChild);
        }
      }
      return count;
    }

    // 素体の差し替えなどでメッシュのサブメッシュ数が変わると、マテリアルスロットの対応がずれたり不足したりする
    private static void WarnIfSlotMismatch(Renderer sourceRenderer, Renderer destRenderer)
    {
      Mesh sourceMesh = GetMesh(sourceRenderer);
      Mesh destMesh = GetMesh(destRenderer);
      if (destMesh == null) return;

      int materialCount = sourceRenderer.sharedMaterials.Length;
      if (materialCount < destMesh.subMeshCount)
      {
        Debug.LogWarning($"[MaterialCopier] '{destRenderer.name}': コピーするマテリアル数（{materialCount}）がコピー先メッシュのサブメッシュ数（{destMesh.subMeshCount}）より少ないため、マテリアルが割り当てられないサブメッシュがあります。", destRenderer);
      }
      else if (sourceMesh != null && sourceMesh.subMeshCount != destMesh.subMeshCount)
      {
        Debug.LogWarning($"[MaterialCopier] '{destRenderer.name}': サブメッシュ数が異なります（From: {sourceMesh.subMeshCount} → To: {destMesh.subMeshCount}）。マテリアルスロットの対応がずれていないか確認してください。", destRenderer);
      }
    }

    private static Mesh GetMesh(Renderer renderer)
    {
      if (renderer is SkinnedMeshRenderer skinnedMeshRenderer) return skinnedMeshRenderer.sharedMesh;
      var meshFilter = renderer.GetComponent<MeshFilter>();
      return meshFilter != null ? meshFilter.sharedMesh : null;
    }
  }
}
#endif