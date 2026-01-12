using UnityEditor;
using UnityEngine;

[CreateAssetMenu(fileName = "TestClassExporter", menuName = "PSOC/TestClassExporter")]
public class TestClassExporter : ScriptableObject
{
    public MonoScript testMonscript;

    public ScriptableObject testScriptableObject;

    [ContextMenu("Export Class Info")]
    public void ExportClassInfo()
    {
        if (testMonscript == null)
        {
            Debug.LogError("No MonoScript assigned.");
            return;
        }

        var classType = testMonscript.GetClass();
        if (classType == null)
        {
            Debug.LogError("Could not get class from MonoScript.");
            return;
        }

        string str = ClassFieldDescriptor.Describe(testMonscript, requireFieldDoc: true);

        // copy to clipboard
        TextEditor te = new TextEditor
        {
            text = str
        };
        te.SelectAll();
        te.Copy();
        Debug.Log("Class description copied to clipboard:\n" + str);


    }

    [ContextMenu("Export ScriptableObject JSON")]
    public void ExportScriptableObjectJson()
    {
        if (testScriptableObject == null)
        {
            Debug.LogError("No ScriptableObject assigned.");
            return;
        }

        string json = AnnotatedSoJson.ToJson(testScriptableObject);

        // copy to clipboard
        TextEditor te = new TextEditor
        {
            text = json
        };
        te.SelectAll();
        te.Copy();
        Debug.Log("ScriptableObject JSON copied to clipboard:\n" + json);
    }
}