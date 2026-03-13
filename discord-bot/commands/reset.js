const { SlashCommandBuilder, EmbedBuilder, Colors, PermissionFlagsBits } = require('discord.js');
const { deleteVerifiedOrder, getVerifiedOrder } = require('../utils/database');

module.exports = {
  data: new SlashCommandBuilder()
    .setName('reset-order')
    .setDescription('Supprime un order vérifié (admin) — permet une re-vérification')
    .setDefaultMemberPermissions(PermissionFlagsBits.ManageGuild)
    .addStringOption((opt) =>
      opt
        .setName('order_id')
        .setDescription('Order ID à réinitialiser')
        .setRequired(true)
    ),

  async execute(interaction) {
    await interaction.deferReply({ ephemeral: true });

    const orderId = interaction.options.getString('order_id').trim();
    const existing = getVerifiedOrder(orderId);

    if (!existing) {
      return interaction.editReply({
        embeds: [
          new EmbedBuilder()
            .setColor(Colors.Orange)
            .setTitle('⚠️ Introuvable')
            .setDescription(`L'order \`${orderId}\` n'est pas dans la base de données.`),
        ],
      });
    }

    deleteVerifiedOrder(orderId);

    return interaction.editReply({
      embeds: [
        new EmbedBuilder()
          .setColor(Colors.Green)
          .setTitle('✅ Order réinitialisé')
          .setDescription(
            `L'order \`${orderId}\` (précédemment vérifié par <@${existing.user_id}>) a été supprimé.\nIl peut maintenant être re-vérifié.`
          ),
      ],
    });
  },
};
