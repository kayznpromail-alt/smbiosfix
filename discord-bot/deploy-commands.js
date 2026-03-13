/**
 * Standalone script to manually (re)deploy slash commands.
 * Run: node deploy-commands.js
 */
require('dotenv').config();

const { REST, Routes } = require('discord.js');
const fs = require('fs');
const path = require('path');

const rest = new REST().setToken(process.env.DISCORD_TOKEN);

const commandData = fs
  .readdirSync(path.join(__dirname, 'commands'))
  .filter((f) => f.endsWith('.js'))
  .map((f) => require(`./commands/${f}`).data.toJSON());

(async () => {
  try {
    if (process.env.GUILD_ID) {
      await rest.put(
        Routes.applicationGuildCommands(process.env.CLIENT_ID, process.env.GUILD_ID),
        { body: commandData }
      );
      console.log(`✅ Deployed ${commandData.length} guild command(s) to guild ${process.env.GUILD_ID}`);
    } else {
      await rest.put(Routes.applicationCommands(process.env.CLIENT_ID), { body: commandData });
      console.log(`✅ Deployed ${commandData.length} global command(s)`);
    }
  } catch (err) {
    console.error('❌ Deploy failed:', err);
  }
})();
