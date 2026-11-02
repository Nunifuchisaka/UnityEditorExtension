#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Collections.Generic;
using System;
using System.Linq;

namespace Nunifuchisaka
{
  /// <summary>
  /// Unitypackageの内容をプレビューし、既存アセットとの競合を確認するエディタ拡張。
  /// </summary>
  public class PackagePreviewer : EditorWindow
  {
    private class AssetInfo { public string Path { get; set; } public string Guid { get; set; } public string Status { get; set; } public string ExistingPath { get; set; } public Texture Icon { get; set; } }
    private List<AssetInfo> _assetInfos = new List<AssetInfo>();
    private Vector2 _scrollPosition;
    private string _selectedPackagePath;
    private bool _isParsing = false;

    [MenuItem("Tools/Nunifuchisaka/Import Package with Preview...", false, 200)]
    private static void ShowWindowMenu()
    {
      string path = EditorUtility.OpenFilePanel("Select Unity Package", "", "unitypackage");
      if (string.IsNullOrEmpty(path)) { return; }
      PackagePreviewer window = GetWindow<PackagePreviewer>(true, "Package Previewer", true);
      window.minSize = new Vector2(500, 300);
      window.StartParsing(path);
    }

    private void StartParsing(string packagePath)
    {
      _selectedPackagePath = packagePath;
      _isParsing = true;
      _assetInfos.Clear();
      Repaint();
      EditorApplication.delayCall += () =>
      {
        try { ParsePackage(_selectedPackagePath); }
        catch (Exception e)
        {
          Debug.LogError($"[PackagePreviewer] Failed to parse package. Error: {e.ToString()}");
          EditorUtility.DisplayDialog("Error", "An unexpected error occurred. See the console for details.", "OK");
        }
        finally { _isParsing = false; Repaint(); }
      };
    }

    private void OnGUI()
    {
      EditorGUILayout.LabelField("Package:", EditorStyles.boldLabel);
      DocumentationLink.Draw();
      EditorGUILayout.SelectableLabel(_selectedPackagePath, EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight));
      EditorGUILayout.Space();
      if (_isParsing) { EditorGUILayout.LabelField("Parsing package contents, please wait..."); return; }
      EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
      EditorGUILayout.LabelField("Package Contents", EditorStyles.boldLabel);
      GUILayout.FlexibleSpace();
      EditorGUILayout.LabelField("Status", GUILayout.Width(80));
      EditorGUILayout.EndHorizontal();
      _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, "box");
      if (_assetInfos.Count == 0) { EditorGUILayout.HelpBox("No assets found in the package.", MessageType.Info); }
      foreach (var info in _assetInfos)
      {
        EditorGUILayout.BeginHorizontal();
        GUILayout.Box(info.Icon, GUIStyle.none, GUILayout.Width(20), GUILayout.Height(20));
        EditorGUILayout.LabelField(new GUIContent(info.Path));
        Color originalColor = GUI.color;
        GUI.color = info.Status == "Overwrite" ? Color.yellow : info.Status == "Rename" ? new Color(1f, 0.6f, 0.3f) : Color.green;
        EditorGUILayout.LabelField(info.Status, GUILayout.Width(80));
        GUI.color = originalColor;
        EditorGUILayout.EndHorizontal();
        if (!string.IsNullOrEmpty(info.ExistingPath))
        {
          EditorGUI.indentLevel++;
          if (info.Status == "Rename")
          {
            EditorGUILayout.LabelField(new GUIContent($"└ Conflicts: {info.ExistingPath}", "同じパスに別のアセット（GUIDが異なる）が既にあるため、上書きされずに別名でインポートされます。"), EditorStyles.miniLabel);
          }
          else
          {
            EditorGUILayout.LabelField(new GUIContent($"└ Overwrites: {info.ExistingPath}", "This is the path of the existing asset in your project that will be overwritten."), EditorStyles.miniLabel);
          }
          EditorGUI.indentLevel--;
        }
      }
      EditorGUILayout.EndScrollView();
      EditorGUILayout.Space();
      if (GUILayout.Button("Open Standard Import Dialog", GUILayout.Height(30)))
      {
        if (!string.IsNullOrEmpty(_selectedPackagePath)) { AssetDatabase.ImportPackage(_selectedPackagePath, true); this.Close(); }
      }
    }

    private void ParsePackage(string path)
    {
      _assetInfos.Clear();
      var tempAssetInfos = new Dictionary<string, AssetInfo>();

      // パッケージ全体をメモリに展開すると、大きなアバターパッケージ（展開後2GB超など）で失敗するため、
      // tarエントリをストリームで順に読み、pathname 以外の中身は読み飛ばす
      using (FileStream fileStream = new FileStream(path, FileMode.Open, FileAccess.Read))
      using (GZipStream gzipStream = new GZipStream(fileStream, CompressionMode.Decompress))
      {
        byte[] header = new byte[512];
        while (ReadFully(gzipStream, header, 512))
        {
          if (header.All(b => b == 0)) { break; }

          string name = Encoding.UTF8.GetString(header, 0, 100).TrimEnd('\0');
          long size = ParseTarSize(header);
          long paddedSize = (size + 511) / 512 * 512;

          string normalizedName = name.StartsWith("./") ? name.Substring(2) : name;
          string[] parts = normalizedName.Split('/');
          bool isPathname = !string.IsNullOrEmpty(name) && !name.Contains("PaxHeader") && !name.Contains("/._")
                            && parts.Length == 2 && parts[1] == "pathname" && size > 0 && size < 64 * 1024;

          if (isPathname)
          {
            byte[] data = new byte[paddedSize];
            if (!ReadFully(gzipStream, data, (int)paddedSize)) { break; }
            // pathname には "Assets/xxx\n00" のように2行目が付く場合があるため、1行目だけを使う
            string assetPath = Encoding.UTF8.GetString(data, 0, (int)size).Split('\n')[0].Trim();
            string guid = parts[0];
            tempAssetInfos[guid] = CreateAssetInfo(guid, assetPath);
          }
          else if (!Skip(gzipStream, paddedSize))
          {
            break;
          }
        }
      }

      _assetInfos.AddRange(tempAssetInfos.Values);
      _assetInfos.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));
    }

    private static AssetInfo CreateAssetInfo(string guid, string assetPath)
    {
      var info = new AssetInfo
      {
        Guid = guid,
        Path = assetPath,
        Status = "New",
        Icon = AssetDatabase.GetCachedIcon(assetPath) ?? EditorGUIUtility.IconContent("DefaultAsset Icon").image
      };

      string existingPath = AssetDatabase.GUIDToAssetPath(guid);
      if (!string.IsNullOrEmpty(existingPath) && AssetDatabase.GetMainAssetTypeAtPath(existingPath) != null)
      {
        // 同じGUIDのアセットが既にある：そのアセットが上書きされる（パスが違う場合は既存のパス側が更新される）
        info.Status = "Overwrite";
        info.ExistingPath = existingPath;
        return info;
      }

      string guidAtPath = AssetDatabase.AssetPathToGUID(assetPath);
      if (!string.IsNullOrEmpty(guidAtPath) && guidAtPath != guid && AssetDatabase.GetMainAssetTypeAtPath(assetPath) != null
          && !AssetDatabase.IsValidFolder(assetPath))
      {
        // 同じパスに別のGUIDのアセットがある：上書きはされず、別名（"xxx 1.png" など）でインポートされる
        info.Status = "Rename";
        info.ExistingPath = assetPath;
      }
      return info;
    }

    private static long ParseTarSize(byte[] header)
    {
      // 8GB以上のファイルは base-256 形式（先頭バイトの最上位ビットが立つ）
      if ((header[124] & 0x80) != 0)
      {
        long value = 0;
        for (int i = 125; i < 136; i++) value = (value << 8) | header[i];
        return value;
      }
      string sizeStr = Encoding.ASCII.GetString(header, 124, 12).TrimEnd('\0', ' ').Trim();
      if (string.IsNullOrEmpty(sizeStr)) return 0;
      try { return Convert.ToInt64(sizeStr, 8); }
      catch { return 0; }
    }

    private static bool ReadFully(Stream stream, byte[] buffer, int count)
    {
      int offset = 0;
      while (offset < count)
      {
        int read = stream.Read(buffer, offset, count - offset);
        if (read <= 0) return false;
        offset += read;
      }
      return true;
    }

    private static bool Skip(Stream stream, long count)
    {
      byte[] buffer = new byte[81920];
      while (count > 0)
      {
        int read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, count));
        if (read <= 0) return false;
        count -= read;
      }
      return true;
    }
  }
}
#endif