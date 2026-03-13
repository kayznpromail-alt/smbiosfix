const { SlashCommandBuilder, EmbedBuilder, Colors, PermissionFlagsBits } = require('discord.js');
const { getStats } = require('../utils/database');

module.exports = {
  data: new SlashCommandBuilder()
    .setName('stats')
    .setDescription('Statistiques des vérifications d\'orders (admin)')
    .setDefaultMemberPermissions(PermissionFlagsBits.ManageGuild),

  async execute(interaction) {
    await interaction.deferReply({ ephemeral: true });

    const { total, today } = getStats(interaction.guildId);

    const embed = new EmbedBuilder()
      .setColor(Colors.Blurple)
      .setTitle('📊 Statistiques — Billgang Order Verifier')
      .addFields(
        { name: 'Total vérifié (tous temps)', value: `**${total}**`, inline: true },
        { name: 'Vérifié aujourd\'hui', value: `**${today}**`, inline: true }
      )
      .setFooter({ text: 'Billgang Order Verifier' })
      .setTimestamp();

    return interaction.editReply({ embeds: [embed] });
  },
};
