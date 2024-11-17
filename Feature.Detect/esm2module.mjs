import fs from 'fs';
import path from 'path';

// Check if a filename and output directory are provided as arguments
if (process.argv.length < 4) {
    console.error('Usage: node createWasm.mjs <path_to_index.js> <output_directory>');
    process.exit(1);
}

// Get the file path and output directory from the arguments
const indexFilePath = process.argv[2];
const outputDirectory = process.argv[3];

// Extract the output file name using the directory name of the index.js file
const outputFileName = path.basename(path.dirname(indexFilePath));
const outputFilePath = path.join(outputDirectory, `${outputFileName}.wasm`);
const outputJsonPath = path.join(outputDirectory, `${outputFileName}.json`);

// Ensure the output directory exists
if (!fs.existsSync(outputDirectory)) {
    fs.mkdirSync(outputDirectory, { recursive: true });
}

// Save the original WebAssembly functions
const OriginalWebAssemblyModule = WebAssembly.Module;
const OriginalWebAssemblyInstantiate = WebAssembly.instantiate;
const OriginalWebAssemblyValidate = WebAssembly.validate;

// Helper function to convert various input types to Uint8Array
function toUint8Array(bufferSource) {
    if (bufferSource instanceof Uint8Array) {
        return bufferSource;
    } else if (Array.isArray(bufferSource)) {
        return new Uint8Array(bufferSource);
    } else if (bufferSource instanceof ArrayBuffer) {
        return new Uint8Array(bufferSource);
    } else if (bufferSource instanceof SharedArrayBuffer) {
        return new Uint8Array(bufferSource);
    } else {
        throw new TypeError('Expected Uint8Array, ArrayBuffer, or Array');
    }
}

// Helper function to write the WASM buffer
function writeWasmBuffer(bufferSource, source) {
    try {
        const uint8Array = toUint8Array(bufferSource);
        fs.writeFileSync(outputFilePath, uint8Array);
        console.log(`Wrote ${uint8Array.length} bytes to ${outputFilePath} (source: ${source})`);
        return uint8Array;
    } catch (error) {
        console.error('Error writing WASM buffer:', error);
        throw error;
    }
}

// Helper function to write metadata
function writeMetadata(metadata) {
    try {
        const metadataWithBytes = {
            ...metadata,
            byteLength: metadata.bytes?.length,
            bytes: Array.from(metadata.bytes || []), // Convert Uint8Array to regular array for JSON
        };
        fs.writeFileSync(outputJsonPath, JSON.stringify(metadataWithBytes, null, 2));
        console.log(`Wrote metadata to ${outputJsonPath}`);
    } catch (error) {
        console.error('Error writing metadata:', error);
        throw error;
    }
}

// Monkey patch WebAssembly.Module
WebAssembly.Module = function(bufferSource) {
    console.log('Intercepted WebAssembly.Module call');
    const bytes = writeWasmBuffer(bufferSource, 'Module');
    writeMetadata({
        source: 'Module',
        timestamp: new Date().toISOString(),
        bytes
    });
    return new OriginalWebAssemblyModule(bufferSource);
};

// Monkey patch WebAssembly.instantiate
WebAssembly.instantiate = function(bufferSource, importObject, options) {
    console.log('Intercepted WebAssembly.instantiate call');
    const bytes = writeWasmBuffer(bufferSource, 'instantiate');

    const metadata = {
        source: 'instantiate',
        timestamp: new Date().toISOString(),
        importObject: importObject || {},
        options: options || {},
        bytes
    };

    writeMetadata(metadata);

    return OriginalWebAssemblyInstantiate(bufferSource, importObject, options);
};

// Monkey patch WebAssembly.validate
WebAssembly.validate = function(bufferSource) {
    console.log('Intercepted WebAssembly.validate call');
    const bytes = writeWasmBuffer(bufferSource, 'validate');

    const metadata = {
        source: 'validate',
        timestamp: new Date().toISOString(),
        bytes
    };

    writeMetadata(metadata);

    return OriginalWebAssemblyValidate(bufferSource);
};

// Dynamically import the index file
(async () => {
    try {
        const module = await import(indexFilePath);
        const result = await module.default();
        console.log('Function executed:', result);
    } catch (error) {
        console.error('Error executing module:', error);
        process.exit(1);
    }
})();