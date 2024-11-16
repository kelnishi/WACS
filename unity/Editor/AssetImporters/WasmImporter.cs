using UnityEngine;
using UnityEditor;
using UnityEditor.AssetImporters;
using System.IO;

[ScriptedImporter(1, "wasm")]
public class WasmImporter : ScriptedImporter
{
    public override void OnImportAsset(AssetImportContext ctx)
    {
        // Load the binary data from the .kelvin file
        byte[] data = File.ReadAllBytes(ctx.assetPath);

        // Create an instance of KelvinAsset and assign the data
        WasmAsset wasmAsset = ScriptableObject.CreateInstance<WasmAsset>();
        wasmAsset.data = data;

        // Add the asset to the import context
        ctx.AddObjectToAsset("Wasm Asset", wasmAsset);
        ctx.SetMainObject(wasmAsset);
    }
}