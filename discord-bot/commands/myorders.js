const { SlashCommandBuilder, EmbedBuilder, Colors } = require('discord.js');
const { getOrdersByUser } = require('../utils/database');

module.exports = {
  data: new SlashCommandBuilder()
    .setName('myorders')
    .setDescription('Affiche les orders que tu as vérifiés'),

  async execute(interaction) {
    await interaction.deferReply({ ephemeral: true });

    const orders = getOrdersByUser(interaction.user.id, interaction.guildId);

    if (!orders.length) {
      return interaction.editReply({
        embeds: [
          new EmbedBuilder()
            .setColor(Colors.Grey)
            .setTitle('📦 Aucun order')
            .setDescription("Tu n'as encore vérifié aucun order sur ce serveur."),
        ],
      });
    }

    const lines = orders.map((o) => {
      const date = new Date(o.verified_at).toLocaleDateString('fr-FR');
      return `• \`${o.order_id}\` — vérifié le ${date}`;
    });

    const embed = new EmbedBuilder()
      .setColor(Colors.Blurple)
      .setTitle('📦 Tes orders vérifiés')
      .setDescription(lines.join('\n'))
      .setFooter({ text: `${orders.length} order(s) vérifié(s)` });

    return interaction.editReply({ embeds: [embed] });
  },
};
