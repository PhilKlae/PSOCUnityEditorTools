using UnityEngine;

[CreateAssetMenu(fileName = "VectorQueryEngineTool", menuName = "PSOC/Tools/VectorQueryEngineTool")]
public class VectorQueryEngineTool : ToolBase
{
    // will trigger re indexing
    [SerializeField] public int ChunkSize = 512;
    [SerializeField] public int chunk_overlap = 64;
    
    // will not trigger re indexing
    [SerializeField] public int SimilarityTopK = 3;
    [SerializeField] public ResponseMode responseMode = ResponseMode.Compact;
    [SerializeField] public bool IncludeText = true;

}

public enum ResponseMode
{
    Compact,
    Detailed
}