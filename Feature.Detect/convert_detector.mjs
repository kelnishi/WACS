import fs from 'fs';
import path from 'path';
import wabt from 'wabt';
import { fileURLToPath } from 'url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

// Check if a directory is provided as argument
if (process.argv.length < 3) {
    console.error('Usage: node createWasm.mjs <input_directory> [output_directory]');
    process.exit(1);
}

// Get the input and output directories from the arguments
const inputDirectory = process.argv[2];
const outputDirectory = process.argv[3] || inputDirectory;

const watPath = path.join(inputDirectory, 'module.wat');
const indexPath = path.join(inputDirectory, 'index.js');

const outputFileName = path.basename(inputDirectory);
const outputFilePath = path.join(outputDirectory, `${outputFileName}.wasm`);
const outputJsonPath = path.join(outputDirectory, `${outputFileName}.json`);

// Ensure the output directory exists
if (!fs.existsSync(outputDirectory)) {
    fs.mkdirSync(outputDirectory, { recursive: true });
}

// Function to extract proposal information from source file
function extractProposalInfo(content) {
    try {
        const proposalMatch = content.match(/;; Proposal: (https:\/\/[^\n]+)/);
        const nameMatch = content.match(/;; Name: ([^\n]+)/);
        const featuresMatch = content.match(/;; Features: ([^\n]+)/);

        return {
            name: nameMatch ? nameMatch[1].trim() : undefined,
            proposal: proposalMatch ? proposalMatch[1].trim() : undefined,
            features: featuresMatch ? featuresMatch[1].trim().split(',').map(f => f.trim()) : undefined
        };
    } catch (error) {
        console.error('Error extracting proposal info:', error);
        return {};
    }
}

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

// Function to handle WAT file
async function processWatFile(watPath, outputBasePath) {
    console.log('Processing WAT file:', watPath);
    try {
        // Read and parse the WAT file
        const watContent = fs.readFileSync(watPath, 'utf8');
        const proposalInfo = extractProposalInfo(watContent);

        // Initialize wabt and convert WAT to WASM
        let features = proposalInfo.features ?? [];
        const wabtInstance = await wabt();
        const wasmModule = wabtInstance.parseWat(
            'module.wat',
            watContent,
            Object.fromEntries(features.map((flag) => [flag, true])),);
        
        const { buffer } = wasmModule.toBinary({});

        // Write WASM file
        const outputWasmPath = outputBasePath + '.wasm';
        fs.writeFileSync(outputWasmPath, Buffer.from(buffer));
        console.log(`Wrote ${buffer.byteLength} bytes to ${outputWasmPath}`);

        const outputFilename = path.basename(outputWasmPath);
        
        // Write metadata
        const metadata = {
            source: 'wat2wasm',
            timestamp: new Date().toISOString(),
            id: outputFileName,
            module: outputFilename,
            ...proposalInfo,
        };

        const outputJsonPath = outputBasePath + '.json';
        fs.writeFileSync(outputJsonPath, JSON.stringify(metadata, null, 2));
        console.log(`Wrote metadata to ${outputJsonPath}`);

        return true;
    } catch (error) {
        console.error('Error processing WAT file:', error);
        return false;
    }
}

let captured = false;
let currentSourceFile = null;

// Helper function to write metadata for JS-generated WASM
function writeMetadata(metadata, sourceFile) {
    try {
        const content = fs.readFileSync(sourceFile, 'utf8');
        const proposalInfo = extractProposalInfo(content);
        const metadataWithBytes = {
            ...metadata,
            ...proposalInfo,
        };

        const outputJsonPath = path.join(outputDirectory, path.basename(path.dirname(sourceFile)) + '.json');
        fs.writeFileSync(outputJsonPath, JSON.stringify(metadataWithBytes, null, 2));
        console.log(`Wrote metadata to ${outputJsonPath}`);
    } catch (error) {
        console.error('Error writing metadata:', error);
        throw error;
    }
}

// Helper function to write the WASM buffer from JS
function writeWasmBuffer(bufferSource, source, sourceFile) {
    try {
        const uint8Array = toUint8Array(bufferSource);
        const outputWasmPath = path.join(outputDirectory, path.basename(path.dirname(sourceFile)) + '.wasm');
        fs.writeFileSync(outputWasmPath, uint8Array);
        console.log(`Wrote ${uint8Array.length} bytes to ${outputWasmPath} (source: ${source})`);
        captured = true;

        const outputFilename = path.basename(outputWasmPath);
        return outputFilename;
    } catch (error) {
        console.error('Error writing WASM buffer:', error);
        throw error;
    }
}

const handler = {
    has(target, prop) {
        // Log the property being checked with "in"
        // console.log(`Checking presence of property "${String(prop)}" in WebAssembly`);
        writeMetadata({
            source: `\'${String(prop)}' in WebAssembly`,
            id: outputFileName,
        }, indexPath);
        
        // Return the result of the default behavior
        return Reflect.has(target, prop);
    }
};

// Create a proxy for the WebAssembly object
const webAssemblyProxy = new Proxy(WebAssembly, handler);

// Monkey patch WebAssembly.Module
webAssemblyProxy.Module = function(bufferSource) {
    const outfile = writeWasmBuffer(bufferSource, 'Module', indexPath);
    writeMetadata({
        source: 'WebAssembly.Module',
        id: outputFileName,
        module: outfile,
    }, indexPath);
    
    return 'Intercepted WebAssembly.Module call';
};

// Monkey patch WebAssembly.instantiate
webAssemblyProxy.instantiate = function(bufferSource, importObject, options) {
    const outfile = writeWasmBuffer(bufferSource, 'instantiate', indexPath);

    const metadata = {
        source: 'WebAssembly.instantiate',
        id: outputFileName,
        module: outfile,
        options: options || {},
    };

    writeMetadata(metadata, indexPath);
    return 'Intercepted WebAssembly.instantiate call';
};

// Monkey patch WebAssembly.validate
webAssemblyProxy.validate = function(bufferSource) {
    const outfile = writeWasmBuffer(bufferSource, 'validate', indexPath);

    const metadata = {
        source: 'WebAssembly.validate',
        id: outputFileName,
        module: outfile,
    };

    writeMetadata(metadata, indexPath);
    return 'Intercepted WebAssembly.validate call';
};

// Replace the original WebAssembly with the proxy
global.WebAssembly = webAssemblyProxy;

// Main function to process directory
async function processDirectory(inputDir) {
    const outputBasePath = path.join(outputDirectory, path.basename(inputDir));

    if (fs.existsSync(watPath)) {
        console.log('Found module.wat');
        return processWatFile(watPath, outputBasePath);
    } else if (fs.existsSync(indexPath)) {
        console.log('Found index.js');
        try {
            // Read and transform the source code
            const module = await import("./"+indexPath);
            const result = await module.default();
            
            console.log('Function executed:', result);
            return true;
        } catch (error) {
            console.error('Error executing JS code:', error);
            return false;
        }
    } else {
        console.error('No module.wat or index.js found in directory');
        return false;
    }
}

// Run the script
processDirectory(inputDirectory).catch(error => {
    console.error('Error:', error);
    process.exit(1);
});