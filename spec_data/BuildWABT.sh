
# -g tells compiler to add debug info
emcmake cmake .. \
    -DCMAKE_BUILD_TYPE=Release \
    -DCMAKE_EXE_LINKER_FLAGS="-s PURE_WASI=1 -s STANDALONE_WASM=1 -s EXPORT_KEEPALIVE=1 -s ERROR_ON_UNDEFINED_SYMBOLS=0 -s ALLOW_MEMORY_GROWTH=1 -Wl,--allow-multiple-definition" \
    -DCMAKE_C_FLAGS="-g" \
    -DCMAKE_CXX_FLAGS="-g"
    
emmake make wasm2wat


