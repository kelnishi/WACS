const fs = require('fs').promises;
const xml2js = require('xml2js');
const path = require('path');
const he = require("he");
const proposals = require('./proposals.json');


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
            
            //Special case garbage
            if (testDef.Id === 'mutable-globals')
                testDef.Proposal = 'https://github.com/WebAssembly/mutable-global';
            if (testDef.Id === 'exceptions')
                testDef.Proposal = " " + testDef.Proposal; 
            
            let url = testDef['Proposal'];
            
            const proposalRecord = proposals.find(record => record.url.toLowerCase() === url.toLowerCase());
            if (proposalRecord)
            {
                testDef['Name'] = proposalRecord.name;
                testDef['Phase'] = proposalRecord.phase;
            }
            else
            {
                testDef['Phase'] = 0;
            }
            
            testDef['outcome'] = test.outcome;
            if (testDef['Source'].includes('in WebAssembly'))
                testDef['outcome'] = 'JS.' + testDef['outcome'];
            else if (testDef.Id == 'big-int')
                testDef['outcome'] = 'JS.' + testDef['outcome'];
            
            tests.push(testDef);
        });
        
        let markdown = [];
        let phase = -1;
        
        tests.sort((a, b) => {
            // Sort by Phase descending
            const phaseComparison = (b.Phase || 0) - (a.Phase || 0);
            if (phaseComparison !== 0) return phaseComparison;

            // If the phases are equal, sort by Id ascending
            return (a.Id || '').localeCompare(b.Id || '');
        });

        markdown.push(`|Proposal |Features|    |`);
        markdown.push(`|------|-------|----|`);
        
        tests.forEach(testDef => {
            if (testDef['Phase'] != phase)
            {
                phase = testDef['Phase'];
                if (phase)
                    markdown.push(`|Phase ${phase}|`);
                else
                    markdown.push(`||`);
            }
            
            let status = '❔';
            switch (testDef['outcome']) {
                case 'Failed': status = '❌'; break;
                case 'Passed': status = '✅'; break;
                case 'JS.Failed': status = '<span title="Browser idioms, not directly supported">🌐</span>'; break;
                case 'JS.Passed': status = '<span title="Browser idiom, but conceptually supported">✳️</span>'; break;
            }
            markdown.push(`|[${testDef['Name']}](${testDef['Proposal']})|${testDef['Features']}|${status}|`);
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