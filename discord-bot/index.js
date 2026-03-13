require('dotenv').config();

const { Client, GatewayIntentBits, Collection, REST, Routes } = require('discord.js');
const fs = require('fs');
const path = require('path');
const BillgangClient = require('./utils/billgang');

// ─── Validate required env vars ──────────────────────────────────────────────
const REQUIRED_ENV = ['DISCORD_TOKEN', 'CLIENT_ID', 'BILLGANG_API_KEY', 'BILLGANG_SHOP_SLUG'];
for (const key of REQUIRED_ENV) {
  if (!process.env[key]) {
    console.error(`[FATAL] Missing environment variable: ${key}`);
    process.exit(1);
  }
}

// ─── Init Billgang API client ─────────────────────────────────────────────────
const billgang = new BillgangClient(
  process.env.BILLGANG_API_KEY,
  process.env.BILLGANG_SHOP_SLUG
);

// ─── Init Discord client ──────────────────────────────────────────────────────
const client = new Client({
  intents: [GatewayIntentBits.Guilds],
});

// ─── Load commands ────────────────────────────────────────────────────────────
client.commands = new Collection();
const commandsPath = path.join(__dirname, 'commands');
for (const file of fs.readdirSync(commandsPath).filter((f) => f.endsWith('.js'))) {
  const command = require(path.join(commandsPath, file));
  if (command.data && command.execute) {
    client.commands.set(command.data.name, command);
    console.log(`[Commands] Loaded: /${command.data.name}`);
  }
}

// ─── Register slash commands on startup ──────────────────────────────────────
client.once('ready', async (bot) => {
  console.log(`[Bot] Logged in as ${bot.user.tag}`);

  const rest = new REST().setToken(process.env.DISCORD_TOKEN);
  const commandData = [...client.commands.values()].map((cmd) => cmd.data.toJSON());

  try {
    // Guild-specific registration (instant) or global (up to 1h delay)
    if (process.env.GUILD_ID) {
      await rest.put(Routes.applicationGuildCommands(process.env.CLIENT_ID, process.env.GUILD_ID), {
        body: commandData,
      });
      console.log(`[Commands] Registered ${commandData.length} guild command(s) in guild ${process.env.GUILD_ID}`);
    } else {
      await rest.put(Routes.applicationCommands(process.env.CLIENT_ID), { body: commandData });
      console.log(`[Commands] Registered ${commandData.length} global command(s) (may take up to 1h to appear)`);
    }
  } catch (err) {
    console.error('[Commands] Failed to register commands:', err);
  }
});

// ─── Handle interactions ──────────────────────────────────────────────────────
client.on('interactionCreate', async (interaction) => {
  if (!interaction.isChatInputCommand()) return;

  const command = client.commands.get(interaction.commandName);
  if (!command) return;

  try {
    await command.execute(interaction, billgang);
  } catch (err) {
    console.error(`[${interaction.commandName}] Unhandled error:`, err);
    const payload = { content: '❌ Une erreur interne s\'est produite. Réessaie plus tard.', ephemeral: true };
    if (interaction.deferred || interaction.replied) {
      await interaction.editReply(payload).catch(() => {});
    } else {
      await interaction.reply(payload).catch(() => {});
    }
  }
});

// ─── Login ────────────────────────────────────────────────────────────────────
client.login(process.env.DISCORD_TOKEN);
