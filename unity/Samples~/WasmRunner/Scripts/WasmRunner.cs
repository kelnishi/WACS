using System;
using System.IO;
using System.Text;
using UnityEngine;
using Wacs.Core;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Core.WASIp1;

public class WasmRunner : MonoBehaviour
{
    [SerializeField] private WasmAsset wasmAsset;

    public string moduleName = "_";
    
    private WasmRuntime _runtime;

    private ModuleInstance _moduleInstance;

    private StringBuilder _output;

    private TextMesh text;
    
    private void OnEnable()
    {
        text = GetComponent<TextMesh>();
        var stream = new MemoryStream(wasmAsset.data);
        var module = BinaryModuleParser.ParseWasm(stream);
        
        _runtime = new WasmRuntime();
        _output = new();
        _runtime.BindHostFunction<Action<char>>(("env", "sayc"), c =>
        {
            _output.Append(c);
        });
        
        _moduleInstance = _runtime.InstantiateModule(module, new RuntimeOptions { SkipModuleValidation = true});
        _runtime.RegisterModule(moduleName, _moduleInstance);
    }

    // Start is called before the first frame update
    void Start()
    {
        var callOptions = new InvokerOptions {
            LogGas = false,
            LogProgressEvery = 0, 
            LogInstructionExecution = InstructionLogging.None,
            CalculateLineNumbers = false,
            CollectStats = false,
        };

        //Wasm/WASI entry points
        if (_moduleInstance.StartFunc != null)
        {
            var caller = _runtime.CreateInvoker<Action>(_moduleInstance.StartFunc, callOptions);
            try
            {
                caller();
            }
            catch (TrapException exc)
            {
                Debug.LogError(exc);
            }
            catch (SignalException exc)
            {
                ErrNo sig = (ErrNo)exc.Signal;
                Debug.LogError($"WasmRunner: Module exited with signal {exc}");
            }
        }
        else if (_runtime.TryGetExportedFunction((moduleName, "main"), out var mainAddr))
        {
            var caller = _runtime.CreateInvoker<Func<Value>>(mainAddr, callOptions);
            try
            {
                int result = caller();
                if (text)
                    text.text = _output.ToString();
                
                Debug.Log($"WasmRunner: Module returned result:{result}");
            }
            catch (TrapException exc)
            {
                Debug.LogError(exc);
            }
            catch (SignalException exc)
            {
                ErrNo sig = (ErrNo)exc.Signal;
                Debug.LogError($"WasmRunner: Module exited with signal {exc}");
            }
        }
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}