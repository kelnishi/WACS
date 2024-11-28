# WACS (C# WebAssembly Interpreter)
![wasm wast spec](https://github.com/kelnishi/WACS/actions/workflows/ci.yml/badge.svg?branch=main) 
![Platform](https://img.shields.io/badge/platform-.NET%20Standard%202.1-blue)
[![License](https://img.shields.io/github/license/kelnishi/WACS)](LICENSE)
[![NuGet](https://img.shields.io/nuget/v/WACS)](https://www.nuget.org/packages/WACS)
[![Downloads](https://img.shields.io/nuget/dt/WACS)](https://www.nuget.org/packages/WACS)
## Overview

**WACS** is a pure C# WebAssembly Interpreter for running WASM modules in .NET environments, including AOT environments like Unity's IL2CPP.

The architecture is my literal interpretation of the WebAssembly spec, so it should be conceptually similar to the OCaml reference interpreter.
The chapters and sections from the spec are commented throughout the source code and should serve as a good reference for others. 

![Wasm in Unity](UnityScreenshot.png)

## Table of Contents

- [Features](#features)
- [Getting Started](#getting-started)
- [Installation](#installation)
- [Usage](#usage)
- [Integration with Unity](#integration-with-unity)
- [Interop Bindings](#interop-bindings)
- [Customization](#customization)
- [Performance](#performance)
- [Roadmap](#roadmap)
- [WebAssembly Feature Extensions](#webassembly-feature-extensions)
- [License](#license)

## Features

- **Pure C# Implementation**: Written in C# 9.0/.NET Standard 2.1. (No unsafe code)
- **No Complex Dependencies**: Uses [FluentValidation](https://github.com/FluentValidation/FluentValidation) and [Microsoft.Extensions.ObjectPool](https://www.nuget.org/packages/Microsoft.Extensions.ObjectPool) as its only dependencies.
- **Unity Compatibility**: Compatible with **Unity 2021.3+** including AOT/IL2CPP modes for iOS.
- **Full WebAssembly MVP Compliance**: Passes the  [WebAssembly spec test suite](https://github.com/WebAssembly/spec/tree/main/test/core).
- **Magical Interop**: Host bindings are validated with reflection, no boilerplate code required.
- **WASI:** Wacs.WASIp1 provides a [wasi\_snapshot\_preview1](https://github.com/WebAssembly/WASI/blob/main/legacy/preview1/docs.md) implementation.

## Getting Started

### Installation

The easiest way to use WACS is to add the package from NuGet

```bash
dotnet add package WACS
dotnet add package WACS.WASIp1
````

If you prefer to build WACS from source, you can clone the repo and build it with the .NET SDK:

```bash
git clone https://github.com/kelnishi/WACS.git
cd WACS
dotnet build
```

## Usage

Basic usage example, how to load and run a WebAssembly module:

```csharp
using System;
using System.IO;
using Wacs.Core;
using Wacs.Core.Runtime;

//Create a runtime
var runtime = new WasmRuntime();

//Bind a host function
//  This can be any regular C# delegate.
//  The type here will be validated against module imports.
runtime.BindHostFunction<Action<char>>(("env", "sayc"), c =>
{
    System.Console.Write(c);
});

//Load a module from a binary file
using var fileStream = new FileStream("HelloWorld.wasm", FileMode.Open);
var module = BinaryModuleParser.ParseWasm(fileStream);

//Instantiate the module
var modInst = runtime.InstantiateModule(module);

//Register the module to add its exported functions to the export table
runtime.RegisterModule("hello", modInst);

//Get the module's exported function
if (runtime.TryGetExportedFunction(("hello", "main"), out var mainAddr))
{
    //For wasm functions you can expect return types as Wacs.Core.Runtime.Value
    //  Value has implicit conversion to many useful primitive types
    var mainInvoker = runtime.CreateInvoker<Func<Value>>(mainAddr);
    
    //Call the wasm function and get the result
    //  Implicit conversion from Value to int
    int result = mainInvoker();
    
    System.Console.Error.WriteLine($"hello.main() => {result}");
}
```

## Integration with Unity

### With Unity Package Manager
1. Window>Package Manager
2. Click + Add package from git URL...
3. Enter the package repo URL:
 ```text
 git@github.com:kelnishi/WACS-Unity.git
 ```
4. Click Add

This will put the DLLs into your project. 
Import the WasmRunner sample to get started.

### Manually
To manually add WACS to a Unity project, you'll need to add the following DLLs to your Assets directory:
- Wacs.Core.dll
- FluentValidation.dll
- Microsoft.Extensions.ObjectPool.dll

Set **Player Settings>Other Settings>Api Compatibility Level** to **.NET Standard 2.1**.

## Interop Bindings

WACS simplifies host function bindings, allowing you to easily call .NET functions from WebAssembly modules.
This allows seamless communication between your host environment and WebAssembly without boilerplate code.
Similarly, calling into wasm code is done by generating a typed delegate.

Example from WASIp1:

```csharp
//Alias your types for readability
using ptr = System.Int32;

//WACS can bit-convert types like Enums and explicit layout structs
[WasmType(nameof(ValType.I32))]
public enum ErrNo : ushort
{
    Success = 0,
    ...
}

//Supply the delegate definition when binding
//  ExecContext is an optional first parameter for Memory and Stack manipulation
runtime.BindHostFunction<Func<ExecContext,ptr,ptr,ErrNo>>(
   (module, "args_get"), ArgsGet);

// WASIp1's args_get
public ErrNo ArgsGet(ExecContext ctx, ptr argvPtr, ptr argvBufPtr)
{
    var mem = ctx.DefaultMemory;
            
    foreach (string arg in _config.Arguments)
    {
        // Copy argument string to argvBufPtr.
        int strLen = mem.WriteUtf8String((uint)argvBufPtr, arg, true);
                
        // Write pointer to argument in argvPtr.
        mem.WriteInt32(argvPtr, argvBufPtr);

        // Update offsets.
        argvBufPtr += strLen;
        argvPtr += sizeof(ptr);
    }

    return ErrNo.Success;
}
```

## Customization

If you'd like to customize the wasm runtime environment, I recommend downloading the full source for examples.

The `Wacs.WASIp1` implementation is a good starting point for how to set up your own library of bindings.
It also contains examples of more advanced usage like binding multiple return values and full operand stack access.

The `Spec.Test` project runs the wasm spec test suite. This also contains examples for binding other runtime environment
objects like Tables, Memories, and Variables.

Custom Instruction implementations can be patched in by replacing or inheriting from `SpecFactory`.

## Performance

WACS is a bytecode (wasm) interpreter running on a bytecode interpreted (or JIT'd) language (CIL/CLR). This is, as you can imagine,
*not a recipe for raw performance*. However, recognizing this dynamic allows us to make certain optimizations to achieve
performance closer to other languages in other VMs.

### The Spec-Defined Implementation
The Wasm Virtual Machine is a stack machine. This means that instructions produce operands, place them on the stack, and then other
instructions consume them by popping them from the stack. WACS's implementation does exactly this with the data structure you might expect 
in C#: `Stack<Value>`. So for every _instruction_, WACS performs some number of Pops and Pushes to this stack. Each Pop or Push 
is a function call to a class and may or may not require a constructor to initialize a Value or memory to be resized for the 
Stack<> operation. This is **extremely** heavy. The CLR is manipulating its function call stack, managing memory, checking bounds,
boxing/unboxing, etc. All for a single WASM instruction! And, while most C# runtimes have these kinds of things heavily optimized,
It's still a lot compared to what our instruction represents. I've taken great care to minimize memory allocation or any extra code
within these critical paths, but the core paradigm of objects and function calls is difficult to avoid.

### In-Memory Transpiling
If it can't be avoided, then what? Acceptance. Here's where we break WASM semantics and go off-road to claw back some performance.
A linear list of WASM instructions can be inverted into an expression tree. Take for example, this sequence:
> i32.const 5   <- Pushes 5 onto the stack
> 
> i32.const 7   <- Pushes 7 onto the stack
> 
> i32.add       <- Pops 7, Pops 5, Pushes 12 onto the stack

For a sequence representing `5+7`, this is performing potentially 8+ function calls, multiple Value.ctors, memory bounds checks, etc.
All this, not even including the actual computation (+). Knowing this, we have an alternative. 

**Expression Tree Compilation**

```
       i32.add
     /         \
i32.const 5  i32.const 7
```
If enabled, WACS will do a linear pass through the instruction sequences and roll up interdependent instructions into directed acyclic graphs.
Instructions are replaced with functionally equivalent expression trees `InstAggregate`. The new aggregate instructions are *in-memory*
and are implemented with pre-built relational functions. Ultimately, these instructions are compiled by the dotnet build process into bytecode
to be run by the runtime. Thus, at runtime aggregate wasm instruction sequences map to pre-compiled implementations (*semi-transpiled*).

How does this differ from executing the wasm instructions linearly with the WACS VM? 
- No OpStack manipulation
- Values are passed directly without casting or boxing
- The CLR's implementation can use hardware more effectively (use registers instead of heap memory)
- Avoids instruction fetching and dispatch

In my testing, this leads to roughly 60% higher instruction processing throughput (10Mips -> 16Mips). These gains are situational however.
Linking of the instructions into a tree cannot 100% be determined across block boundaries. So in these cases, the transpiler just passes
the sequence through unaltered. So WASM code with lots of function calls or branches will see less benefit.

There's still some headroom for optimization. Optimization is an ongoing process and I have a few other strategies yet to implement.

My plan for 1.0 includes:
- Prebaked super-instructions for memory operations
- Replace some object pools with pre-computed statics
- Implement the above transpiling for SIMD instructions (currently only i32/i64/f32/f64 instructions are optimized)
- Provide an API for 3rd party super-instruction optimization

## Roadmap

The current TODO list includes:

- **ExecAsync**: Thread scheduling and advanced gas metering (basically JSPI, but C# Tasks)
- **Wasm Garbage Collection**: Support  wasm-gc and heaptypes.
- **Text Format Parsing**: Add support for WebAssembly text format.
- **WASI p1 Test Suite**: Validate WASIp1 with the test suite for improved standard compliance.
- **WASI p2 and Component Model**: Implement the component model proposal.
- **SIMD Intrinsics**: Add hardware-accelerated SIMD (software implementation included in Wacs.Core).
- **Unity Bindings for SDL**: Implement SDL2 with Unity bindings.
- **JavaScript Proxy Bindings**: Maybe support common JS env functions.

## WebAssembly Feature Extensions
WACS is based on the [WebAssembly Core 2 spec](https://www.w3.org/TR/wasm-core-2/).
I am implementing and adding support for as many phase 5 extensions as I can.
Progress depends mostly on complexity and non-javascriptiness.

Harnessed results from [wasm-feature-detect](https://github.com/GoogleChromeLabs/wasm-feature-detect) as compares to [other runtimes](https://webassembly.org/features/):

|Proposal |Features|    |
|------|-------|----|
|Phase 5|
|[JavaScript BigInt to WebAssembly i64 integration](https://github.com/WebAssembly/JS-BigInt-integration)||<span title="Browser idiom, but conceptually supported">✳️</span>|
|[Bulk memory operations](https://github.com/webassembly/bulk-memory-operations)||✅|
|[Extended Constant Expressions](https://github.com/WebAssembly/extended-const)|extended_const|✅|
|[Garbage collection](https://github.com/WebAssembly/gc)|gc|❌|
|[Multiple memories](https://github.com/WebAssembly/multi-memory)|multi-memory|✅|
|[Multi-value](https://github.com/WebAssembly/multi-value)|multi_value|✅|
|[Import/Export of Mutable Globals](https://github.com/WebAssembly/mutable-global)||✅|
|[Reference Types](https://github.com/WebAssembly/reference-types)||✅|
|[Relaxed SIMD](https://github.com/webassembly/relaxed-simd)|relaxed_simd|✅|
|[Non-trapping float-to-int conversions](https://github.com/WebAssembly/nontrapping-float-to-int-conversions)||✅|
|[Sign-extension operators](https://github.com/WebAssembly/sign-extension-ops)||✅|
|[Fixed-width SIMD](https://github.com/webassembly/simd)||✅|
|[Tail call](https://github.com/webassembly/tail-call)|tail_call|✅|
|[Typed Function References](https://github.com/WebAssembly/function-references)|function-references|❌|
|Phase 4|
|[Exception handling](https://github.com/WebAssembly/exception-handling)|exceptions|❌|
|[JS String Builtins](https://github.com/WebAssembly/js-string-builtins)||❌|
|[Memory64](https://github.com/WebAssembly/memory64)|memory64|❌|
|[Threads](https://github.com/webassembly/threads)|threads|❌|
|Phase 3|
|[JS Promise Integration](https://github.com/WebAssembly/js-promise-integration)|jspi|<span title="Browser idiom, but conceptually supported">✳️</span>|
|[Type Reflection for WebAssembly JavaScript API](https://github.com/WebAssembly/js-types)|type-reflection|<span title="Browser idioms, not directly supported">🌐</span>|
||
|[Legacy Exception Handling]( https://github.com/WebAssembly/exception-handling)|exceptions|❌|
|[Streaming Compilation](https://webassembly.github.io/spec/web-api/index.html#streaming-modules)|streaming_compilation|<span title="Browser idioms, not directly supported">🌐</span>|

This table was generated with the Feature.Detect test harness.

## Sponsorship & Collaboration

I built and maintain WACS as a solo developer. 

If you find it useful, please consider supporting me through sponsorship or work opportunities. 
Your support can help me continue improving WACS to make WebAssembly accessible for everyone. 

[Sponsor me](https://github.com/sponsors/kelnishi) or [connect with me on LinkedIn](https://www.linkedin.com/in/kelnishi) if you're interested in collaborating!

## License

WACS is distributed under the [Apache 2.0 License](./LICENSE). This permissive license allows you to use WACS freely in both open and closed source projects.

---

I would love for you to get involved and contribute to WACS! Whether it's bug fixes, new features, or improvements to documentation, your help can make WACS better for everyone.

**Star this project on GitHub if you find WACS helpful!** ⭐

