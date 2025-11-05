// DataBucketConfig.cs

using System.IO;
using UnityEditor;
using UnityEngine;

[CreateAssetMenu(fileName = "DataBucket", menuName = "PSOC/Data Bucket Configuration")]
public class DataBucketConfig : ScriptableObject
{
    [Header("Bucket Configuration")]
    public string bucketName = "new-bucket";
    public DefaultAsset sourceFolder;
    [TextArea] public string description;

    [Header("Sync")]
    // When true this bucket will be excluded from any bulk sync operations
    public bool excludeFromSync = false;

    public string FolderPath => 
        sourceFolder ? Path.GetFullPath(Path.Combine(Application.dataPath, "..", AssetDatabase.GetAssetPath(sourceFolder))) : string.Empty;
}