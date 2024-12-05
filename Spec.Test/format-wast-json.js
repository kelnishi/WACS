const fs = require('fs');

// Get the input file from the command-line arguments
const inputFile = process.argv[2];

if (!inputFile) {
    console.error('Please provide a JSON file as an argument.');
    process.exit(1);
}

// Read the input JSON file
const jsonStr = fs.readFileSync(inputFile, 'utf8');
const jsonObj = JSON.parse(jsonStr);

// Function to serialize the JSON object with custom formatting
function reserialize(obj, indent = 0) {
    if (Array.isArray(obj)) {
        return serializeArray(obj, indent);
    } else if (typeof obj === 'object' && obj !== null) {
        return serializeObject(obj, indent);
    } else {
        return JSON.stringify(obj);
    }
}

function serializeObject(obj, indent) {
    let result = '{\n';
    const keys = Object.keys(obj);
    keys.forEach((key, index) => {
        result += '  '.repeat(indent + 1) + JSON.stringify(key) + ': ';
        if (key === 'commands' && Array.isArray(obj[key])) {
            // Special handling for the "commands" array
            result += serializeArray(obj[key], indent + 1);
        } else {
            result += reserialize(obj[key], indent + 1);
        }
        if (index < keys.length - 1) {
            result += ',\n'; // Insert newline after each top-level property
        }
    });
    result += '\n' + '  '.repeat(indent) + '}';
    return result;
}

function serializeArray(arr, indent) {
    let result = '[\n';
    arr.forEach((item, index) => {
        result += '  '.repeat(indent + 1) + JSON.stringify(item);
        if (index < arr.length - 1) {
            result += ',\n';
        }
    });
    result += '\n' + '  '.repeat(indent) + ']';
    return result;
}

const formattedJson = reserialize(jsonObj);

fs.writeFileSync(inputFile, formattedJson, 'utf8');

console.log(`Formatted JSON has been written to ${inputFile}`);