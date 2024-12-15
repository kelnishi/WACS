#!/usr/bin/env node

/**
 * Self-Contained Node.js Script to:
 * 1. Run `dotnet test` with TRX logger.
 * 2. Parse `TestResults.trx` to extract successful test names.
 * 3. Output the successful test names to `successfulTests.json`.
 * 4. Delete the `TestResults.trx` file.
 *
 * Usage:
 *   node run-tests.js
 */

const { spawn } = require('child_process');
const fs = require('fs');
const path = require('path');

// Configuration
const TRX_DIR = 'TestResults';
const TRX_FILE = 'TestResults.trx';
const JSON_OUTPUT_FILE = 'successfulTests.json';
const DOTNET_TEST_COMMAND = `dotnet test --logger "trx;LogFileName=${TRX_FILE}"`;

/**
 * Executes the dotnet test command.
 * @returns {Promise<void>}
 */
function runDotnetTest() {
    return new Promise((resolve, reject) => {
        console.log('Running `dotnet test`...');
        const process = spawn(DOTNET_TEST_COMMAND, { shell: true });

        // Forward stdout and stderr to the console in real-time
        process.stdout.on('data', (data) => {
            console.log(data.toString());
        });

        process.stderr.on('data', (data) => {
            console.error(data.toString());
        });

        process.on('close', (code) => {
            console.log(`dotnet test exited with code ${code}`);
            // Resolve the promise regardless of the exit code
            resolve();
        });

        // Handle any error with spawning the process.
        process.on('error', (err) => {
            console.error(`Failed to start process: ${err}`);
            resolve(); // Resolve even if there is an error starting the process
        });
    });
}

/**
 * Parses the TRX file to extract successful test names.
 * @param {string} trxFilePath - Path to the TRX file.
 * @returns {Promise<string[]>} - Array of successful test names.
 */
function parseTrxFile(trxFilePath) {
    return new Promise((resolve, reject) => {
        console.log('Parsing TRX file...');
        fs.readFile(trxFilePath, 'utf8', (err, data) => {
            if (err) {
                return reject(`Failed to read TRX file: ${err.message}`);
            }

            try {
                // Regular expression to match <UnitTestResult> elements with outcome="Passed"
                const regex = /<UnitTestResult([^>]*?testName="([^"]+)"[^>]*?outcome="Passed"[^>]*?)>/g;

                let match;
                let successfulTests = [];

                while ((match = regex.exec(data)) !== null) {
                    const str = match[2];
                    if (!str.includes("RunWast("))
                        continue;
                    
                    const fileregex = /([^/]+\.wast)/;
                    const filematch = str.match(fileregex);
                    
                    if (filematch) {
                        successfulTests.push(filematch[0]);
                    }
                }
                
                //Sort the successfulTests
                successfulTests = [...new Set(successfulTests)];
                successfulTests.sort();

                console.log(`Total successful tests found: ${successfulTests.length}`);
                resolve(successfulTests);
            } catch (parseError) {
                reject(`Failed to parse TRX file: ${parseError.message}`);
            }
        });
    });
}

/**
 * Writes the successful test names to a JSON file.
 * @param {string[]} successfulTests - Array of successful test names.
 * @param {string} outputPath - Path to the output JSON file.
 * @returns {Promise<void>}
 */
function writeJsonFile(successfulTests, outputPath) {
    return new Promise((resolve, reject) => {
        console.log(`Writing successful test names to ${outputPath}...`);
        const jsonContent = JSON.stringify(successfulTests, null, 2);
        fs.writeFile(outputPath, jsonContent, 'utf8', (err) => {
            if (err) {
                return reject(`Failed to write JSON file: ${err.message}`);
            }
            console.log('JSON file created successfully.');
            resolve();
        });
    });
}

/**
 * Deletes the TRX file.
 * @param {string} trxFilePath - Path to the TRX file.
 * @returns {Promise<void>}
 */
function deleteTrxFile(trxFilePath) {
    return new Promise((resolve, reject) => {
        console.log(`Deleting TRX file: ${trxFilePath}...`);
        fs.unlink(trxFilePath, (err) => {
            if (err) {
                // If deletion fails, log the error but don't reject
                console.error(`Failed to delete TRX file: ${err.message}`);
                return resolve();
            }
            console.log('TRX file deleted successfully.');
            resolve();
        });
    });
}

/**
 * Main function to orchestrate the steps.
 */
async function main() {
    try {
        await runDotnetTest();

        const trxFilePath = path.resolve(__dirname, TRX_DIR + "/" + TRX_FILE);
        if (!fs.existsSync(trxFilePath)) {
            throw new Error(`TRX file not found at ${trxFilePath}`);
        }

        const successfulTests = await parseTrxFile(trxFilePath);

        successfulTests.push("comments.wast");
        
        const testSettingsPath = path.resolve(__dirname, 'testsettings.json');
        if (fs.existsSync(testSettingsPath)) {
            const testSettings = JSON.parse(fs.readFileSync(testSettingsPath, 'utf8'));
            testSettings.SkipWasts = successfulTests;
            fs.writeFileSync(testSettingsPath, JSON.stringify(testSettings, null, 2), 'utf8');
            console.log('Updated SkipWasts in testsettings.json.');
        } else {
            console.error(`testsettings.json not found at ${testSettingsPath}`);
        }
        
        
        // const jsonOutputPath = path.resolve(__dirname, JSON_OUTPUT_FILE);
        // await writeJsonFile(successfulTests, jsonOutputPath);

        // await deleteTrxFile(trxFilePath);

        console.log('All steps completed successfully.');
    } catch (error) {
        console.error('An error occurred:', error);
        process.exit(1);
    }
}

// Execute the main function
main();