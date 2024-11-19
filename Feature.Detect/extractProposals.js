const fs = require('fs');
const path = require('path');

// File paths
const readmeFilePath = path.join(__dirname, 'proposals', 'README.md');
const finishedFilePath = path.join(__dirname, 'proposals', 'finished-proposals.md');
const outputFilePath = path.join(__dirname, 'proposals.json');

// Function to parse the README content
function parseMarkdown(content) {
    const proposals = [];
    const urls = {};

    // Extract proposal URLs from the bottom of the file
    const urlRegex = /^\[([^\]]+)\]:\s*(https?:\/\/[^\s]+)/gm;
    let urlMatch;
    while ((urlMatch = urlRegex.exec(content)) !== null) {
        urls[urlMatch[1]] = urlMatch[2];
    }

    // Extract phases, tables, and rows
    const phaseRegex = /### Phase (\d+) - .*?\n|# Finished/g;
    const rowRegex = /\| \[([^\]]+)\]\[([^\]]+)\]/g;

    const phaseChunks = content.split(phaseRegex).slice(1);
    
    let phase = 5;
    phaseChunks.forEach(chunk => {
        if (!chunk)
            return;
        
        const parsedPhase = parseInt(chunk.trim(), 10);
        if (!isNaN(parsedPhase)) {
            phase = parsedPhase;
            return;
        }
        if (chunk.trim()) {
            while ((rowMatch = rowRegex.exec(chunk)) !== null) {
                const id = rowMatch[2];
                const name = rowMatch[1];
                const url = urls[id] || null; // Match with the parsed URLs
                if (url)
                    proposals.push({ id, name, phase, url });
            }
        }
    });
    
    return proposals;
}

// Main function
function main() {
    try {
        const readme = fs.readFileSync(readmeFilePath, 'utf8');
        const props = parseMarkdown(readme);
        
        const finished = fs.readFileSync(finishedFilePath, 'utf8');
        const doneProps = parseMarkdown(finished);
        
        const mergedProps = [...props, ...doneProps];
        
        fs.writeFileSync(outputFilePath, JSON.stringify(mergedProps, null, 2), 'utf8');
        console.log(`Extracted proposals saved to ${outputFilePath}`);
    } catch (error) {
        console.error('Error:', error.message);
    }
}

// Execute script
main();