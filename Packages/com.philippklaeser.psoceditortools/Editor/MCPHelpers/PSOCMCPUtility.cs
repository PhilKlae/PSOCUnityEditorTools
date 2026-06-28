using UnityEditor;
using UnityEngine;

public class PSOCMCPUtility
{
    public static void CreateFolders(string folderPath)
    {
        var parts = folderPath.Split('/');
        var current = parts[0];

        for (int i = 1; i < parts.Length; i++)
        {
            var next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(current, parts[i]);
            }
            current = next;
        }
    }
}
