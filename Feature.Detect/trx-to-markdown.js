const fs = require('fs').promises;
const xml2js = require('xml2js');
const path = require('path');
const he = require("he");

class TrxToMarkdown {
    constructor() {
        this.parser = new xml2js.Parser();
    }

    async convertFile(inputPath, outputPath) {
        try {
            const xmlData = await fs.readFile(inputPath, 'utf8');
            const result = await this.parser.parseStringPromise(xmlData);
            const markdown = this.generateMarkdown(result);
            await fs.writeFile(outputPath, markdown);
            console.log(`Successfully converted ${inputPath} to ${outputPath}`);
        } catch (error) {
            console.error('Error converting file:', error);
            throw error;
        }
    }

    generateMarkdown(trxData) {
        const testRun = trxData.TestRun;
        const results = testRun.Results[0].UnitTestResult;
        
        let tests = [];
        results.forEach(result => {
            const test = result['$'];
            const jsonStartIndex = test.testName.indexOf('{');
            const jsonString = test.testName.slice(jsonStartIndex, -1);

            function decodeHtmlEntities(str) {
                const he = require('he');
                return he.decode(str);
            }

            const json = decodeHtmlEntities(jsonString);
            const testDef = JSON.parse(json);
            testDef['outcome'] = test.outcome;
            
            tests.push(testDef);
        });
        
        let markdown = [];
        
        markdown.push(`|Proposal |Features|    |`);
        markdown.push(`|------|-------|----|`);
        
        tests.sort((a, b) => (a.Id || '').localeCompare(b.Id || ''));
        
        tests.forEach(testDef => {
            markdown.push(`|[${testDef['Name']}](${testDef['Proposal']})|${testDef['Features']}|${testDef['outcome'] === 'Failed'?'❌':'✅'}|`);
        });
        
        return markdown.join('\n');
    }
}

// CLI implementation
async function main() {
    if (process.argv.length !== 4) {
        console.log('Usage: node trx-to-markdown.js <input.trx> <output.md>');
        process.exit(1);
    }

    const inputFile = process.argv[2];
    const outputFile = process.argv[3];

    try {
        const converter = new TrxToMarkdown();
        await converter.convertFile(inputFile, outputFile);
    } catch (error) {
        console.error('Conversion failed:', error);
        process.exit(1);
    }
}

if (require.main === module) {
    main();
}

module.exports = TrxToMarkdown;