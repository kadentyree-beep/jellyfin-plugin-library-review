module.exports = { testDir: './tests/ui', use: { browserName: 'chromium', channel: process.env.PW_CHANNEL || undefined, headless: true }, reporter: 'list' };
