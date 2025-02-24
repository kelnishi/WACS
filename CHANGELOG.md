# Changelog

## [0.7.4] Performance
### Link-time optimization
- Instantiated functions are now flattened into a tape at link time
- Labels, branches, and function call targets are now computed during link
- Addressable store elements can now be precomputed and cached during link
- block, loop, trytable, and end instructions are now flagged as nops and will not incur a dispatch function call
### OpStack resident locals
- Local variables are now allocated on the stack
- Local variable operations now have improved cache locality 
- This refactor is prep for link-time register computation

## [0.7.3]
- Reimplemented AOT compatible invoker bindings

## [0.7.2]
- removing Linq.Expression for AOT compatibility

## [0.7.1]
- fixes to CreateInvoker binding

## [0.7.0]
- wasm-3.0 spec support
- exnref/tag support
- memory64 support
- multi-memory support (enabled)

## [0.6.0]
- wasm-gc extension
- function-references extension

## [0.3.0]
- Implemented JSPI-like async binding and execution
- Hooked up more super-instruction threading

## [0.2.0]
- Implemented super-instruction threading
- Precomputed (non-allocating) block labels

## [0.1.6]
- Updating to latest dll
- Fixing package layout
- Fixing Sample importer

## [0.1.4]
- Initial project setup for Unity.
